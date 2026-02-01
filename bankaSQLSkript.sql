
CREATE DATABASE fyjoBanka;

USE fyjoBanka;

CREATE TABLE IF NOT EXISTS accounts (
    account_number INT NOT NULL,
    bank_code VARCHAR(45) NOT NULL,
    balance DECIMAL(15, 2) NOT NULL DEFAULT 0.00,
    
    PRIMARY KEY (account_number, bank_code),
    INDEX idx_bank_code (bank_code),
    
    CONSTRAINT chk_balance_positive CHECK (balance >= 0)
);