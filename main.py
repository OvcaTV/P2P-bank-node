import socket
import threading
import json
from decimal import Decimal
from typing import Dict
import os
import random
import logging


class Account:
    def __init__(self, number: int):
        self.number = number
        self.balance = Decimal('0')

    def to_dict(self):
        return {
            'number': self.number,
            'balance': str(self.balance)
        }

    @staticmethod
    def from_dict(data):
        acc = Account(data['number'])
        acc.balance = Decimal(data['balance'])
        return acc


class Bank:
    def __init__(self, host='0.0.0.0', port=8888, timeout=300):
        self.host = host
        self.port = port
        self.timeout = timeout  # Timeout sec
        self.accounts: Dict[int, Account] = {}
        self.next_account_number = 10001
        self.lock = threading.Lock()
        self.ip_address = self._get_local_ip()
        self.data_file = 'bank_data.json'
        self.running = True
        self.client_threads = []
        self._load_state()

        logging.info("Banka inicializována")
        logging.info(f"IP adresa: {self.ip_address}")
        logging.info(f"Port: {self.port}")
        logging.info(f"Timeout: {self.timeout}s")

    def _get_local_ip(self) -> str:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(('8.8.8.8', 80))
            ip = s.getsockname()[0]
            s.close()
            return ip
        except Exception:
            return '127.0.0.1'

    def _load_state(self):
        if os.path.exists(self.data_file):
            try:
                with open(self.data_file, 'r') as f:
                    data = json.load(f)
                    self.next_account_number = data['next_account_number']
                    for acc_data in data['accounts']:
                        acc = Account.from_dict(acc_data)
                        self.accounts[acc.number] = acc
                print(f"[INFO] Načten stav: {len(self.accounts)} účtů")
                logging.info(f"Načten stav: {len(self.accounts)} účtů")
            except Exception as e:
                print(f"[ERROR] Chyba při načítání stavu: {e}")
                logging.error(f"Chyba při načítání stavu: {e}")

    def _save_state(self):
        try:
            data = {
                'next_account_number': self.next_account_number,
                'accounts': [acc.to_dict() for acc in self.accounts.values()]
            }
            with open(self.data_file, 'w') as f:
                json.dump(data, f, indent=2)
            logging.debug("Stav banky uložen")
        except Exception as e:
            print(f"[ERROR] Chyba při ukládání stavu: {e}")
            logging.error(f"Chyba při ukládání stavu: {e}")

    def bank_code(self) -> str:
        return self.ip_address

    def account_create(self) -> str:
        with self.lock:
            account_number = self.next_account_number
            self.next_account_number += 1

            account = Account(account_number)
            self.accounts[account_number] = account

            self._save_state()
            result = f"{account_number}/{self.ip_address}"
            logging.info(f"Vytvořen nový účet: {result}")
            return result

    def account_deposit(self, account_full: str, amount_str: str) -> str:
        try:
            if '/' not in account_full:
                raise Exception("Neplatný formát účtu (očekáváno: číslo/IP)")

            account_num_str, ip = account_full.split('/')
            account_num = int(account_num_str)
            amount = Decimal(amount_str)

            if ip != self.ip_address:
                raise Exception(f"Účet není v této bance (očekáváno: {self.ip_address})")

            if amount <= 0:
                raise Exception("Částka musí být kladná")

            with self.lock:
                if account_num not in self.accounts:
                    raise Exception("Účet neexistuje")

                account = self.accounts[account_num]
                account.balance += amount

                self._save_state()
                logging.info(f"Vklad: {amount} Kč na účet {account_full}, nový zůstatek: {account.balance} Kč")
                return f"OK: Vloženo {amount} Kč na účet {account_full}, nový zůstatek: {account.balance} Kč"

        except ValueError:
            raise Exception("Neplatný formát čísla nebo částky")
        except Exception as e:
            raise Exception(str(e))

    def account_withdrawal(self, account_full: str, amount_str: str) -> str:
        try:
            if '/' not in account_full:
                raise Exception("Neplatný formát účtu (očekáváno: číslo/IP)")

            account_num_str, ip = account_full.split('/')
            account_num = int(account_num_str)
            amount = Decimal(amount_str)

            if ip != self.ip_address:
                raise Exception(f"Účet není v této bance (očekáváno: {self.ip_address})")

            if amount <= 0:
                raise Exception("Částka musí být kladná")

            with self.lock:
                if account_num not in self.accounts:
                    raise Exception("Účet neexistuje")

                account = self.accounts[account_num]
                if account.balance < amount:
                    raise Exception(f"Nedostatečný zůstatek (dostupné: {account.balance} Kč)")

                account.balance -= amount

                self._save_state()
                logging.info(f"Výběr: {amount} Kč z účtu {account_full}, nový zůstatek: {account.balance} Kč")
                return f"OK: Vybráno {amount} Kč z účtu {account_full}, nový zůstatek: {account.balance} Kč"

        except ValueError:
            raise Exception("Neplatný formát čísla nebo částky")
        except Exception as e:
            raise Exception(str(e))

    def account_balance(self, account_full: str) -> str:
        try:
            if '/' not in account_full:
                raise Exception("Neplatný formát účtu (očekáváno: číslo/IP)")

            account_num_str, ip = account_full.split('/')
            account_num = int(account_num_str)

            if ip != self.ip_address:
                raise Exception(f"Účet není v této bance (očekáváno: {self.ip_address})")

            with self.lock:
                if account_num not in self.accounts:
                    raise Exception("Účet neexistuje")

                account = self.accounts[account_num]
                return f"{account.balance}"

        except ValueError:
            raise Exception("Neplatný formát čísla účtu")
        except Exception as e:
            raise Exception(str(e))

    def execute_command(self, command: str) -> str:
        parts = command.strip().split()

        if not parts:
            return "ERROR: Prázdný příkaz"

        cmd = parts[0].upper()
        logging.debug(f"Zpracovávám příkaz: {cmd}")

        try:
            if cmd == 'BC':
                return self.bank_code()

            elif cmd == 'AC':
                return self.account_create()

            elif cmd == 'AD':
                if len(parts) != 3:
                    raise Exception("Použití: AD číslo/IP částka")
                return self.account_deposit(parts[1], parts[2])

            elif cmd == 'AW':
                if len(parts) != 3:
                    raise Exception("Použití: AW číslo/IP částka")
                return self.account_withdrawal(parts[1], parts[2])

            elif cmd == 'AB':
                if len(parts) != 2:
                    raise Exception("Použití: AB číslo/IP")
                return self.account_balance(parts[1])

            else:
                raise Exception(f"Neznámý příkaz '{cmd}'")

        except Exception as e:
            logging.warning(f"Chyba při zpracování příkazu '{cmd}': {str(e)}")
            return f"ERROR: {str(e)}"

    def handle_client(self, client_socket, address):
        try:
            welcome = f"P2P Banka {self.ip_address}\nPřipojeno. Podporované příkazy: BC, AC, AD, AW, AB\n"
            client_socket.send(welcome.encode('utf-8'))

            while self.running:
                data = client_socket.recv(1024).decode('utf-8').strip()

                if not data:
                    break

                print(f"[{address}] Příkaz: {data}")
                logging.info(f"[{address}] Příkaz: {data}")

                response = self.execute_command(data)

                client_socket.send(f"{response}\n".encode('utf-8'))
                print(f"[{address}] Odpověď: {response}")
                logging.info(f"[{address}] Odpověď: {response}")

        except Exception as e:
            print(f"[ERROR] Chyba při obsluze klienta {address}: {e}")
            logging.error(f"Chyba při obsluze klienta {address}: {e}")

        finally:
            client_socket.close()
            print(f"[DISCONNECT] Odpojeno: {address}")
            logging.info(f"Odpojeno: {address}")

    def start(self):
        server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

        server_socket.settimeout(self.timeout)

        if self.port == 0 or self.port is None:
            self.port = random.randint(65525, 65535)

        try:
            server_socket.bind((self.host, self.port))
            server_socket.listen(5)

            print(f"P2P Banka spuštěna")
            print(f"IP adresa: {self.ip_address}")
            print(f"Port: {self.port}")
            print(f"Timeout: {self.timeout} s")
            logging.info(f"Server running on {self.host}:{self.port}")

            while self.running:
                try:
                    client_socket, address = server_socket.accept()

                    client_thread = threading.Thread(
                        target=self.handle_client,
                        args=(client_socket, address),
                        name=f"Client-{address[0]}:{address[1]}"
                    )
                    client_thread.daemon = False
                    client_thread.start()
                    self.client_threads.append(client_thread)

                    self.client_threads = [t for t in self.client_threads if t.is_alive()]

                except socket.timeout:
                    continue

        except KeyboardInterrupt:
            logging.info("Server shutdown (Ctrl+C)")
            self.shutdown()
        except Exception as e:
            print(f"Server errir: {e}")
            logging.error(f"Critical server error: {e}")
        finally:
            server_socket.close()
            print("Seever off")
            logging.info("Seever off")

    def shutdown(self):
        self.running = False

        thread_active = [t for t in self.client_threads if t.is_alive()]
        print(f"Shutting off {len(thread_active)} threads")
        logging.info(f"Shutting off {len(thread_active)} threads")

        for thread in thread_active:
            thread.join(timeout=5)
            if thread.is_alive():
                logging.warning(f"Vlákno {thread.name} nebylo ukončeno v timeoutu")

        self._save_state()

        logging.info(f"Finální stav: {len(self.accounts)} účtů")
        logging.info("=" * 60)


if __name__ == "__main__":
    logging.basicConfig(
        filename='log.log',
        level=logging.INFO,
        format='%(asctime)s - %(levelname)s - %(message)s',
        encoding='utf-8'
    )

    bank = Bank(host='0.0.0.0', port=random.randint(65525, 65535), timeout=5)
    bank.start()