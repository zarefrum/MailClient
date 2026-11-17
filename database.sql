-- =====================================================
--  Консольный почтовый клиент — база данных (PostgreSQL)
--  Сначала создайте базу mail_client (pgAdmin: Databases → Create → Database),
--  затем откройте Query Tool на ней и выполните этот файл.
-- =====================================================

-- 1. Таблицы и связи
CREATE TABLE users (
    id         SERIAL PRIMARY KEY,
    username   VARCHAR(50)  UNIQUE NOT NULL,
    password   VARCHAR(100) NOT NULL,
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE TABLE messages (
    id           SERIAL PRIMARY KEY,
    sender_id    INTEGER NOT NULL REFERENCES users(id),
    recipient_id INTEGER NOT NULL REFERENCES users(id),
    subject      VARCHAR(200) NOT NULL,
    body         TEXT,
    sent_at      TIMESTAMP DEFAULT NOW(),
    is_read      BOOLEAN DEFAULT FALSE
);

-- 2. VIEW (представление): письмо сразу с именами отправителя и получателя.
--    Вместо того чтобы каждый раз писать два JOIN, обращаемся к view как к таблице.
CREATE VIEW message_list AS
SELECT m.id,
       m.sender_id,
       s.username AS sender,
       m.recipient_id,
       r.username AS recipient,
       m.subject,
       m.body,
       m.sent_at,
       m.is_read
FROM messages m
JOIN users s ON s.id = m.sender_id
JOIN users r ON r.id = m.recipient_id;

-- 3. Индексы: «входящие» ищут по recipient_id, «отправленные» — по sender_id.
--    Без индекса БД перебирает ВСЕ письма, с индексом — сразу находит нужные.
CREATE INDEX idx_messages_recipient ON messages(recipient_id);
CREATE INDEX idx_messages_sender    ON messages(sender_id);

-- Проверка
SELECT id, username, created_at FROM users;
