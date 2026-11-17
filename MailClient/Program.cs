using System;
using System.Data.Common;
using System.Text;
using Npgsql;                 // ВАРИАНТ А: PostgreSQL
// using MySqlConnector;      // ВАРИАНТ Б: MySQL

namespace MailClient
{
    class Program
    {
        // ВАРИАНТ А — PostgreSQL. Замените postgres в конце на свой пароль.
        const string ConnString =
            "Host=localhost;Port=5432;Database=mail_client;Username=postgres;Password=postgres";

        // ВАРИАНТ Б — MySQL
        // const string ConnString =
        //     "Server=localhost;Port=3306;Database=mail_client;User ID=root;Password=;";

        // Id и логин того, кто сейчас вошёл. 0 — никто не вошёл.
        static int currentUserId = 0;
        static string currentUserName = "";

        // ===================== Единственные два места, зависящие от БД =====================

        // 1) Создание подключения
        static DbConnection NewConnection()
        {
            return new NpgsqlConnection(ConnString);        // A: PostgreSQL
            // return new MySqlConnection(ConnString);      // Б: MySQL
        }

        // 2) Распознавание ошибки «нарушение уникальности» (такой логин уже есть)
        static bool IsDuplicate(DbException ex)
        {
            return ex is PostgresException pg && pg.SqlState == "23505";     // A
            // return ex is MySqlException my && my.Number == 1062;          // Б
        }

        // Маленький помощник: добавить параметр @name в запрос
        static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;                 // в SQL — @name
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // ===================== Меню =====================

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (!Console.IsInputRedirected)
                Console.InputEncoding = Encoding.Unicode;   // чтобы русские буквы читались нормально

            Console.WriteLine("=== Консольный почтовый клиент ===");

            // Проверяем, что база доступна
            try
            {
                using (var conn = NewConnection())
                {
                    conn.Open();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Не удалось подключиться к базе: " + ex.Message);
                Console.WriteLine("Проверьте пароль в строке подключения (ConnString).");
                Console.ReadKey();
                return;
            }

            while (true)
            {
                Console.WriteLine();
                if (currentUserId == 0)
                {
                    Console.WriteLine("1. Регистрация");
                    Console.WriteLine("2. Вход");
                    Console.WriteLine("0. Выход из программы");
                    Console.Write("> ");
                    string choice = Console.ReadLine();
                    if (choice == null) return;

                    try
                    {
                        switch (choice.Trim())
                        {
                            case "1": Register(); break;
                            case "2": Login(); break;
                            case "0": return;
                            default: Console.WriteLine("Нет такого пункта."); break;
                        }
                    }
                    catch (DbException ex)
                    {
                        Console.WriteLine("Ошибка базы данных: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("Вы вошли как: " + currentUserName);
                    Console.WriteLine("1. Написать письмо");
                    Console.WriteLine("2. Входящие");
                    Console.WriteLine("3. Отправленные");
                    Console.WriteLine("4. Прочитать письмо");
                    Console.WriteLine("5. Выйти из аккаунта");
                    Console.WriteLine("0. Выход из программы");
                    Console.Write("> ");
                    string choice = Console.ReadLine();
                    if (choice == null) return;

                    try
                    {
                        switch (choice.Trim())
                        {
                            case "1": SendMessage(); break;
                            case "2": ShowInbox(); break;
                            case "3": ShowSent(); break;
                            case "4": ReadMessage(); break;
                            case "5":
                                currentUserId = 0;
                                currentUserName = "";
                                Console.WriteLine("Вы вышли из аккаунта.");
                                break;
                            case "0": return;
                            default: Console.WriteLine("Нет такого пункта."); break;
                        }
                    }
                    catch (DbException ex)
                    {
                        Console.WriteLine("Ошибка базы данных: " + ex.Message);
                    }
                }
            }
        }

        static string Ask(string question)
        {
            Console.Write(question);
            string s = Console.ReadLine();
            return s == null ? "" : s.Trim();
        }

        // ===================== Шаг 3. Регистрация и вход =====================

        static void Register()
        {
            string login = Ask("Логин: ");
            string password = Ask("Пароль: ");

            if (login == "" || password == "")
            {
                Console.WriteLine("Логин и пароль не могут быть пустыми.");
                return;
            }

            using (var conn = NewConnection())
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO users (username, password) VALUES (@u, @p)";
                    AddParam(cmd, "@u", login);
                    AddParam(cmd, "@p", password);
                    try
                    {
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Пользователь «" + login + "» зарегистрирован. Теперь войдите.");
                    }
                    catch (DbException ex) when (IsDuplicate(ex))
                    {
                        // UNIQUE на username не даёт создать двух одинаковых пользователей
                        Console.WriteLine("Такой логин уже занят.");
                    }
                }
            }
        }

        static void Login()
        {
            string login = Ask("Логин: ");
            string password = Ask("Пароль: ");

            using (var conn = NewConnection())
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    // Только параметры! Никаких "... WHERE username = '" + login + "'"
                    cmd.CommandText = "SELECT id FROM users WHERE username = @u AND password = @p";
                    AddParam(cmd, "@u", login);
                    AddParam(cmd, "@p", password);

                    object result = cmd.ExecuteScalar();
                    if (result == null)
                    {
                        Console.WriteLine("Неверный логин или пароль.");
                        return;
                    }
                    currentUserId = Convert.ToInt32(result);
                    currentUserName = login;
                    Console.WriteLine("Добро пожаловать, " + login + "!");
                }
            }
        }

        // ===================== Шаг 4. Отправка письма =====================

        static void SendMessage()
        {
            string to = Ask("Кому (логин): ");
            string subject = Ask("Тема: ");
            string body = Ask("Текст: ");

            if (subject == "")
            {
                Console.WriteLine("Тема не может быть пустой.");
                return;
            }

            using (var conn = NewConnection())
            {
                conn.Open();

                // Находим id получателя по логину
                int recipientId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM users WHERE username = @u";
                    AddParam(cmd, "@u", to);
                    object result = cmd.ExecuteScalar();
                    if (result == null)
                    {
                        Console.WriteLine("Пользователь «" + to + "» не найден.");
                        return;
                    }
                    recipientId = Convert.ToInt32(result);
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "INSERT INTO messages (sender_id, recipient_id, subject, body) " +
                        "VALUES (@s, @r, @subj, @body)";
                    AddParam(cmd, "@s", currentUserId);
                    AddParam(cmd, "@r", recipientId);
                    AddParam(cmd, "@subj", subject);
                    AddParam(cmd, "@body", body);
                    cmd.ExecuteNonQuery();
                }
            }
            Console.WriteLine("Письмо отправлено.");
        }

        // ===================== Шаг 5. Входящие и отправленные (JOIN) =====================

        static void ShowInbox()
        {
            using (var conn = NewConnection())
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    // ОДИН запрос с JOIN: сразу получаем и письмо, и логин отправителя.
                    // (Без JOIN пришлось бы для каждого письма делать отдельный запрос — это N+1.)
                    cmd.CommandText =
                        "SELECT m.id, u.username, m.subject, m.sent_at, m.is_read " +
                        "FROM messages m " +
                        "JOIN users u ON u.id = m.sender_id " +
                        "WHERE m.recipient_id = @me " +
                        "ORDER BY m.sent_at DESC";
                    AddParam(cmd, "@me", currentUserId);

                    using (var r = cmd.ExecuteReader())
                    {
                        Console.WriteLine("--- Входящие ---");
                        int count = 0;
                        while (r.Read())
                        {
                            count++;
                            string mark = r.GetBoolean(4) ? " " : "*";   // * — не прочитано
                            Console.WriteLine(string.Format("{0} #{1}  от {2,-12} {3:dd.MM.yyyy HH:mm}  {4}",
                                mark, r.GetInt32(0), r.GetString(1), r.GetDateTime(3), r.GetString(2)));
                        }
                        if (count == 0) Console.WriteLine("Писем нет.");
                        else Console.WriteLine("(* — не прочитано)");
                    }
                }
            }
        }

        static void ShowSent()
        {
            using (var conn = NewConnection())
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    // Здесь используем VIEW message_list — JOIN уже спрятан внутри представления
                    cmd.CommandText =
                        "SELECT id, recipient, subject, sent_at, is_read " +
                        "FROM message_list " +
                        "WHERE sender_id = @me " +
                        "ORDER BY sent_at DESC";
                    AddParam(cmd, "@me", currentUserId);

                    using (var r = cmd.ExecuteReader())
                    {
                        Console.WriteLine("--- Отправленные ---");
                        int count = 0;
                        while (r.Read())
                        {
                            count++;
                            string status = r.GetBoolean(4) ? "прочитано" : "не прочитано";
                            Console.WriteLine(string.Format("  #{0}  кому {1,-12} {2:dd.MM.yyyy HH:mm}  {3}  [{4}]",
                                r.GetInt32(0), r.GetString(1), r.GetDateTime(3), r.GetString(2), status));
                        }
                        if (count == 0) Console.WriteLine("Писем нет.");
                    }
                }
            }
        }

        static void ReadMessage()
        {
            string input = Ask("Номер письма: ").TrimStart('#');
            int id;
            if (!int.TryParse(input, out id))
            {
                Console.WriteLine("Нужно ввести число.");
                return;
            }

            using (var conn = NewConnection())
            {
                conn.Open();
                bool isRecipient;
                using (var cmd = conn.CreateCommand())
                {
                    // Письмо можно открыть, только если вы его отправитель или получатель
                    cmd.CommandText =
                        "SELECT sender, recipient, subject, body, sent_at, recipient_id " +
                        "FROM message_list " +
                        "WHERE id = @id AND (sender_id = @me OR recipient_id = @me)";
                    AddParam(cmd, "@id", id);
                    AddParam(cmd, "@me", currentUserId);

                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read())
                        {
                            Console.WriteLine("Письмо не найдено.");
                            return;
                        }
                        Console.WriteLine("От:    " + r.GetString(0));
                        Console.WriteLine("Кому:  " + r.GetString(1));
                        Console.WriteLine("Дата:  " + r.GetDateTime(4).ToString("dd.MM.yyyy HH:mm"));
                        Console.WriteLine("Тема:  " + r.GetString(2));
                        Console.WriteLine();
                        Console.WriteLine(r.IsDBNull(3) ? "" : r.GetString(3));
                        isRecipient = r.GetInt32(5) == currentUserId;
                    }
                }

                // Если письмо открыл получатель — отмечаем как прочитанное
                if (isRecipient)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE messages SET is_read = TRUE WHERE id = @id";
                        AddParam(cmd, "@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
