using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TCPServer
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            AppConfig config = LoadConfig();

            if (config == null || config.Database == null)
            {
                Console.WriteLine("Selhalo nacitani konfigurace");
                Console.ReadKey();
                return;
            }

            string serverIp = GetLocalIPAddress();

            Console.WriteLine("Pokus o pripojeni");
            try
            {
                using (var conn = DbConnectionFactory.Create(config.Database))
                {
                    Console.WriteLine("Uspesne pripojeno k databazi\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pripojeni k databazi selhalo: {ex.Message} :'-(");
                Console.WriteLine("Stisknete cokoliv na klavesnici pro pokracovani");
                Console.ReadKey();
                return;
            }
            Console.WriteLine($"IP adresa:   {serverIp,-30}");
            Console.WriteLine($"Port: {config.Port,-30}");

            BankServer server = new BankServer(config.Port, config.Database);

            var serverTask = server.Start();

            Console.WriteLine("Press 'Q' to quit the server...\n");
            while (Console.ReadKey(true).Key != ConsoleKey.Q)
            {
            }
            server.Stop();
            await serverTask;
        }

        static string GetLocalIPAddress()
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

        static AppConfig LoadConfig()
        {
            string configPath = "../../../config.json";

            try
            {
                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"Config nenalezen");
                    CreateDefaultConfig(configPath);
                }

                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return config ?? new AppConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                return new AppConfig();
            }
        }

        static void CreateDefaultConfig(string path)
        {
            var defaultConfig = new AppConfig
            {
                Port = 8888,
                Database = new DbConfig
                {
                    Host = "localhost",
                    Database = "fyjoBanka",
                    User = "root",
                    Password = "student"
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(defaultConfig, options);
            File.WriteAllText(path, json);
        }
    }
}