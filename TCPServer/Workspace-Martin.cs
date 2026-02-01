using MySql.Data.MySqlClient;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TCPServer
{
    public class DbConfig
    {
        public string Host { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
    }

    public class AppConfig
    {
        public DbConfig Database { get; set; }
        public int Port { get; set; } = 8888;
    }

    public static class DbConnectionFactory
    {
        public static MySqlConnection Create(DbConfig cfg)
        {
            var cs = $"Server={cfg.Host};Database={cfg.Database};Uid={cfg.User};Pwd={cfg.Password};";
            var conn = new MySqlConnection(cs);
            conn.Open();
            return conn;
        }
    }

    public class Bank
    {
        private string ipAddress;
        private DbConfig dbConfig;
        private int nextAccountNumber;
        private object lockObject;

        public Bank(DbConfig dbConfig)
        {
            this.dbConfig = dbConfig;
            this.ipAddress = GetLocalIPAddress();
            this.lockObject = new object();
            this.nextAccountNumber = LoadNextAccountNumber();
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        private int LoadNextAccountNumber()
        {
            try
            {
                using (var conn = DbConnectionFactory.Create(dbConfig))
                {
                    var cmd = new MySqlCommand(
                        "SELECT MAX(account_number) FROM accounts WHERE bank_code = @bankCode", conn);
                    cmd.Parameters.AddWithValue("@bankCode", ipAddress);

                    var result = cmd.ExecuteScalar();
                    if (result == DBNull.Value || result == null)
                    {
                        return 10001; // První účet
                    }
                    return Convert.ToInt32(result) + 1;
                }
            }
            catch
            {
                return 10001;
            }
        }

        public string GetBankIP() => ipAddress;

        // BC
        public string BankCode()
        {
            return ipAddress;
        }

        // AC

        public string AccountCreate()
        {
            lock (lockObject){
                try
                {
                    using (var conn = DbConnectionFactory.Create(dbConfig))
                    {
                        var cmd = new MySqlCommand(
                            "INSERT INTO `fyjobanka`.`accounts` (`account_number`, `bank_code`) VALUES (@accountNumber, @ipAddr);\r\n", conn);

                        Random ran = new Random();
                        int x = ran.Next(10000, 99999);

                        cmd.Parameters.AddWithValue("@accountNumber", x);
                        cmd.Parameters.AddWithValue("@ipAddr", ipAddress);


                        var result = cmd.ExecuteNonQuery();

                        return "AC " + x.ToString() + "/" + ipAddress.ToString();
                    }
                }
                catch (Exception ex)
                {
                    return $"error: {ex.Message}";
                }
            }
        }

        // AD
        public string AccountDeposit(string accountRef, decimal amount)
        {
            try
            {
                var parts = accountRef.Split('/');
                if (parts.Length != 2)
                    return "ERROR: Invalid account format. Use number/IP";

                int accountNumber = int.Parse(parts[0]);
                string bankCode = parts[1];

                if (bankCode != ipAddress)
                    return $"ERROR: Account belongs to different bank ({bankCode})";

                if (amount <= 0)
                    return "ERROR: Amount must be positive";

                using (var conn = DbConnectionFactory.Create(dbConfig))
                {
                    var checkCmd = new MySqlCommand(
                        "SELECT balance FROM accounts WHERE account_number = @accountNumber AND bank_code = @bankCode",
                        conn);
                    checkCmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                    checkCmd.Parameters.AddWithValue("@bankCode", bankCode);

                    var result = checkCmd.ExecuteScalar();
                    if (result == null)
                        return $"ERROR: Account {accountNumber} does not exist";

                    decimal currentBalance = Convert.ToDecimal(result);
                    decimal newBalance = currentBalance + amount;

                    var updateCmd = new MySqlCommand(
                        "UPDATE accounts SET balance = balance + @amount WHERE account_number = @accountNumber AND bank_code = @bankCode",
                        conn);
                    updateCmd.Parameters.AddWithValue("@amount", amount);
                    updateCmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                    updateCmd.Parameters.AddWithValue("@bankCode", bankCode);
                    updateCmd.ExecuteNonQuery();

                    return $"OK: Deposited {amount} to account {accountNumber}. New balance: {newBalance}";
                }
            }
            catch (FormatException)
            {
                return "ERROR: Invalid account number format";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        // BA
        public string BankAmount()
        {
            try
            {
                using (var conn = DbConnectionFactory.Create(dbConfig))
                {
                    var cmd = new MySqlCommand(
                        "SELECT COALESCE(SUM(balance), 0) FROM accounts WHERE bank_code = @bankCode",
                        conn);
                    cmd.Parameters.AddWithValue("@bankCode", ipAddress);

                    var result = cmd.ExecuteScalar();
                    return Convert.ToDecimal(result).ToString();
                }
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        // AB
        public string AccountBalance(string accountRef)
        {
            try
            {
                var parts = accountRef.Split('/');
                if (parts.Length != 2)
                    return "ERROR: Invalid account format. Use number/IP";

                int accountNumber = int.Parse(parts[0]);
                string bankCode = parts[1];

                if (bankCode != ipAddress)
                    return $"ERROR: Account belongs to different bank ({bankCode})";

                using (var conn = DbConnectionFactory.Create(dbConfig))
                {
                    var cmd = new MySqlCommand(
                        "SELECT balance FROM accounts WHERE account_number = @accountNumber AND bank_code = @bankCode",
                        conn);
                    cmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                    cmd.Parameters.AddWithValue("@bankCode", bankCode);

                    var result = cmd.ExecuteScalar();
                    if (result == null)
                        return $"ERROR: Account {accountNumber} does not exist";

                    return Convert.ToDecimal(result).ToString();
                }
            }
            catch (FormatException)
            {
                return "ERROR: Invalid account number format";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }


        // AW
        public string AccountWithdrawal(string accountRef, decimal amount)
        {
            lock (lockObject)
            {
                try
                {
                    var parts = accountRef.Split('/');
                    if (parts.Length != 2)
                        return "error: Invalid account format";

                    int accountNumber = int.Parse(parts[0]);
                    string bankCode = parts[1];

                    if (bankCode != ipAddress)
                        return $"error: Account belongs to different bank";

                    if (amount <= 0)
                        return "error: Amount must be positive";

                    using (var conn = DbConnectionFactory.Create(dbConfig))
                    {
                        // Získej zůstatek
                        var selectCmd = new MySqlCommand(
                            "SELECT `balance` FROM `fyjobankaaccounts` WHERE `account_number` = @accountNumber AND `bank_code` = @bankCode",
                            conn);
                        selectCmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                        selectCmd.Parameters.AddWithValue("@bankCode", bankCode);

                        var result = selectCmd.ExecuteScalar();
                        if (result == null)
                            return $"error: Account does not exist";

                        decimal currentBalance = Convert.ToDecimal(result);

                        if (currentBalance < amount)
                            return $"error: Insufficient funds";

                        decimal newBalance = currentBalance - amount;

                        // Aktualizuj zůstatek
                        var updateCmd = new MySqlCommand(
                            "UPDATE `fyjobankaaccounts` SET `balance` = @newBalance WHERE `account_number` = @accountNumber AND `bank_code` = @bankCode",
                            conn);
                        updateCmd.Parameters.AddWithValue("@newBalance", newBalance);
                        updateCmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                        updateCmd.Parameters.AddWithValue("@bankCode", bankCode);
                        updateCmd.ExecuteNonQuery();

                        return $"AW {newBalance}";
                    }
                }
                catch (Exception ex)
                {
                    return $"error: {ex.Message}";
                }
            }
        }

        //AR
        public string AccountRemove(string accountRef)
        {
            lock (lockObject)
            {
                try
                {
                    var parts = accountRef.Split('/');
                    if (parts.Length != 2)
                        return "error: Invalid account format";

                    int accountNumber = int.Parse(parts[0]);
                    string bankCode = parts[1];

                    if (bankCode != ipAddress)
                        return $"error: Account belongs to different bank";

                    using (var conn = DbConnectionFactory.Create(dbConfig))
                    {
                        // Zkontroluj zůstatek
                        var checkCmd = new MySqlCommand(
                            "SELECT `balance` FROM `fyjobankaaccounts` WHERE `account_number` = @accountNumber AND `bank_code` = @bankCode",
                            conn);
                        checkCmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                        checkCmd.Parameters.AddWithValue("@bankCode", bankCode);

                        var result = checkCmd.ExecuteScalar();
                        if (result == null)
                            return $"error: Account does not exist";

                        decimal balance = Convert.ToDecimal(result);
                        if (balance != 0)
                            return $"error: Account has non-zero balance";

                        // Smaž účet
                        var deleteCmd = new MySqlCommand(
                            "DELETE FROM `fyjobankaaccounts` WHERE `account_number` = @accountNumber AND `bank_code` = @bankCode",
                            conn);
                        deleteCmd.Parameters.AddWithValue("@accountNumber", accountNumber);
                        deleteCmd.Parameters.AddWithValue("@bankCode", bankCode);
                        deleteCmd.ExecuteNonQuery();

                        return "AR ok";
                    }
                }
                catch (Exception ex)
                {
                    return $"error: {ex.Message}";
                }
            }
        }


        //BN
        public string BankNumber()
        {
            lock (lockObject)
            {
                try
                {
                    using (var conn = DbConnectionFactory.Create(dbConfig))
                    {
                        var cmd = new MySqlCommand(
                            "SELECT COUNT(*) FROM `fyjobankaaccounts` WHERE `bank_code` = @bankCode",
                            conn);
                        cmd.Parameters.AddWithValue("@bankCode", ipAddress);

                        var result = cmd.ExecuteScalar();
                        return "BN " + result.ToString();
                    }
                }
                catch (Exception ex)
                {
                    return $"error: {ex.Message}";
                }
            }
        }



        public string ExecuteCommand(string command)
        {
            string cmd;

            Console.WriteLine(command);
            var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                cmd = command;
            }
            else
            {
                cmd = parts[0].ToUpper();
            }


            

            try
            {
                switch (cmd)
                {
                    case "BC":
                        return BankCode();

                    case "AD":
                        if (parts.Length < 3)
                            return "ERROR: Usage: AD account/IP amount";
                        return AccountDeposit(parts[1], decimal.Parse(parts[2]));

                    case "BA":
                        return BankAmount();

                    case "AB":
                        if (parts.Length < 2)
                            return "ERROR: Usage: AB account/IP";
                        return AccountBalance(parts[1]);

                    case "AC":
                        return AccountCreate();

                    case "AW":
                        if (parts.Length < 3)
                            return "ERROR: Usage: AW account/IP amount";
                        return AccountWithdrawal(parts[1], decimal.Parse(parts[2]));

                    case "AR":
                        if (parts.Length < 2)
                            return "ERROR: Usage: AR account/IP";
                        return AccountRemove(parts[1]);

                    case "BN":
                        return BankNumber();

                    default:
                        return "ERROR: Neznamy prikaz";

                }
            }

            catch (FormatException)
            {
                return "ERROR: Invalid number format";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }
    }

    public class BankServer
    {
        private Bank bank;
        private TcpListener listener;
        private int port;
        private bool isRunning;

        public BankServer(int port, DbConfig dbConfig)
        {
            this.port = port;
            this.bank = new Bank(dbConfig);
            this.isRunning = false;
        }

        public async Task Start()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                isRunning = true;

                while (isRunning)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClient(client));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
        }

        private async Task HandleClient(TcpClient client)
        {
            string clientEndpoint = client.Client.RemoteEndPoint.ToString();
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected: {clientEndpoint}");

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    byte[] buffer = new byte[1024];

                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                            break;

                        string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{clientEndpoint}] Command: {command}");

                        string response = bank.ExecuteCommand(command);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{clientEndpoint}] Response: {response}");

                        byte[] responseBytes = Encoding.UTF8.GetBytes(response + " ");
                        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client error [{clientEndpoint}]: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client disconnected: {clientEndpoint}");
            }
        }

        public void Stop()
        {
            isRunning = false;
            listener?.Stop();
        }
    }

    public class WorkspaceMartin
    {

        public async Task RunAsync()
        {
        }

        public static void Main(string[] args)
        {
            var instance = new WorkspaceMartin();
            instance.RunAsync();
        }
    }
}
