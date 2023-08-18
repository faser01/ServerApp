using System.Data.SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServerApp
{
    public partial class Form1 : Form
    {
        [Serializable]
        public class ServerSettings
        {
            public string ServerIp { get; set; }
            public int ServerPort { get; set; }
        }
        private const string DatabaseFile = "database.db";
        private const string UsersTable = "users";
        private const string TasksTable = "tasks";
        private const string BigBossUsername = "BigBoss";
        private const string User1 = "User1";
        private const string User2 = "User2";

        private TcpListener tcpListener;
        private List<TcpClient> connectedClients;

        public Form1()
        {
            InitializeComponent();

            // При запуске сервера создаем базу данных и таблицы, если их еще нет
            CreateDatabase();
        }
        private void btnStartServer_Click(object sender, EventArgs e)
        {
            StartServer();
        }


        private void btnStopServer_Click_1(object sender, EventArgs e)
        {
            StopServer();
        }




        private void CreateDatabase()
        {
            // Проверяем, существует ли файл базы данных
            bool databaseExists = File.Exists(DatabaseFile);

            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={DatabaseFile}"))
            {
                connection.Open();

                if (!databaseExists)
                {
                    // Если база данных не существует, создаем таблицы
                    using (SQLiteCommand command = new SQLiteCommand($@"
                CREATE TABLE IF NOT EXISTS {UsersTable} (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL UNIQUE,
                    password TEXT NOT NULL
                );", connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    using (SQLiteCommand command = new SQLiteCommand($@"
                CREATE TABLE IF NOT EXISTS {TasksTable} (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId INTEGER NOT NULL,
                    task TEXT NOT NULL,
                    FOREIGN KEY (userId) REFERENCES {UsersTable}(id)
                );", connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    // Если нет пользователя BigBoss, создаем его
                    if (!CheckIfUserExists(BigBossUsername, connection))
                    {
                        string hashedPassword = HashPassword("bigboss_password");
                        InsertUser(BigBossUsername, hashedPassword, connection);
                    }

                    if (!CheckIfUserExists(User1, connection))
                    {
                        string hashedPassword = HashPassword("user1_password");
                        InsertUser(User1, hashedPassword, connection);
                       
                    }

                    if (!CheckIfUserExists(User2, connection))
                    {
                        string hashedPassword = HashPassword("user2_password");
                        InsertUser(User2, hashedPassword, connection);
                       
                    }

                    // Добавляем данные в таблицу tasks
                    using (SQLiteCommand insertTaskCommand = new SQLiteCommand($@"
                        INSERT INTO {TasksTable} (userId, task)
                        VALUES
                        ((SELECT id FROM {UsersTable} WHERE username = @Username1), @Task1),
                        ((SELECT id FROM {UsersTable} WHERE username = @Username2), @Task2),
                        ((SELECT id FROM {UsersTable} WHERE username = @Username2), @Task3);", connection)){
                        insertTaskCommand.Parameters.AddWithValue("@Username1", User1);
                        insertTaskCommand.Parameters.AddWithValue("@Username2", User2);
                        insertTaskCommand.Parameters.AddWithValue("@Task1", "Task description 1");
                        insertTaskCommand.Parameters.AddWithValue("@Task2", "Task description 2");
                        insertTaskCommand.Parameters.AddWithValue("@Task3", "Task description 3");
                        insertTaskCommand.ExecuteNonQuery();
                    }
                }
            }
        }



       

        public void InsertTask(int userId, string task, SQLiteConnection connection)
        {
            try
            {
                using (SQLiteCommand command = new SQLiteCommand($"INSERT INTO {TasksTable} (userId, task) VALUES (@userId, @task)", connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@task", task);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // Обработка ошибок, если необходимо
                MessageBox.Show($"Ошибка при добавлении задачи: {ex.Message}");
            }
        }
        private bool CheckIfUserExists(string username, SQLiteConnection connection)
        {
            using (SQLiteCommand command = new SQLiteCommand($"SELECT COUNT(*) FROM {UsersTable} WHERE username=@username", connection))
            {
                command.Parameters.AddWithValue("@username", username);
                long count = (long)command.ExecuteScalar();
                return count > 0;
            }
        }

        private void InsertUser(string username, string password, SQLiteConnection connection)
        {
            using (SQLiteCommand command = new SQLiteCommand($"INSERT INTO {UsersTable} (username, password) VALUES (@username, @password)", connection))
            {
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@password", password);
                command.ExecuteNonQuery();
            }
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] hashedBytes = sha256.ComputeHash(passwordBytes);
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private void StartServer()
        {
            try
            {
                string serverIp = txtServerIp.Text;
                int serverPort = int.Parse(txtServerPort.Text);

                // Создаем сервер и начинаем прослушивать подключения
                tcpListener = new TcpListener(IPAddress.Parse(serverIp), serverPort);
                tcpListener.Start();
                lblStatus.Text = "Сервер запущен.";

                // Пока сервер работает, обрабатываем входящие подключения асинхронно
                connectedClients = new List<TcpClient>();
                Task.Run(async () =>
                {
                    while (true)
                    {
                        try
                        {
                            TcpClient client = await tcpListener.AcceptTcpClientAsync();
                            connectedClients.Add(client);

                            // Обработка клиента асинхронно
                            await Task.Run(() => HandleClient(client));
                        }
                        catch (Exception ex)
                        {
                            // Обработка ошибок подключения
                            Console.WriteLine($"Ошибка при подключении клиента: {ex.Message}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска сервера: {ex.Message}");
            }
        }

        private void StopServer()
        {
            // Закрываем все подключения и останавливаем сервер
            if (connectedClients != null)
            {
                foreach (TcpClient client in connectedClients)
                {
                    client.Close();
                }
            }
            tcpListener?.Stop();
            lblStatus.Text = "Сервер остановлен.";
        }

        private async Task HandleClient(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                while (true)
                {
                    // Читаем запрос от клиента
                    byte[] buffer = new byte[4096];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    byte[] messageBytes = new byte[bytesRead];
                    Array.Copy(buffer, messageBytes, bytesRead);
                    string message = Encoding.UTF8.GetString(messageBytes);

                    // Обрабатываем запрос и отправляем ответ
                    string response = await ProcessRequest(message);
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обработки клиента: {ex.Message}");
            }
            finally
            {
                client.Close();
                connectedClients.Remove(client);
            }
        }

      

        private async Task<bool> RegisterUser(string username, string password)
        {
            try
            {
                using (SQLiteConnection connection = new SQLiteConnection($"Data Source={DatabaseFile}"))
                {
                    await connection.OpenAsync();

                    // Проверяем, существует ли пользователь с таким именем
                    string checkQuery = $"SELECT COUNT(*) FROM {UsersTable} WHERE username = @username";
                    using (SQLiteCommand checkCommand = new SQLiteCommand(checkQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@username", username);
                        long count = (long)await checkCommand.ExecuteScalarAsync();
                        if (count > 0)
                        {
                            // Пользователь с таким именем уже существует
                            return false;
                        }
                    }

                    // Хэшируем пароль и добавляем пользователя
                    string hashedPassword = HashPassword(password);
                    string insertQuery = $"INSERT INTO {UsersTable} (username, password) VALUES (@username, @password)";
                    using (SQLiteCommand insertCommand = new SQLiteCommand(insertQuery, connection))
                    {
                        insertCommand.Parameters.AddWithValue("@username", username);
                        insertCommand.Parameters.AddWithValue("@password", hashedPassword);
                        await insertCommand.ExecuteNonQueryAsync();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка регистрации пользователя: {ex.Message}");
                return false;
            }
        }

        private async Task<string> ProcessRequest(string request)
        {
            string[] parts = request.Split(':');
            string command = parts[0];

            if (command == "GET_TASKS")
            {
                // Получаем список дел пользователя
                string username = parts[1];
                List<string> tasks = await GetTasks(username);
                return Serialize(tasks);
            }
            else if (command == "ADD_TASK")
            {
                // Добавляем новое дело для пользователя
                string username = parts[1];
                string task = parts[2];
                await AddTask(username, task);
                return "OK";
            }
            else if (command == "REGISTER")
            {
                string username = parts[1];
                string password = parts[2];

                bool isRegistered = await RegisterUser(username, password);
                return isRegistered ? "SUCCESS" : "FAILURE";
            }

            return "Unknown command";
        }

        private async Task<List<string>> GetTasks(string username)
        {
            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={DatabaseFile};Version=3;"))
            {
                await connection.OpenAsync();
                long userId = await GetUserId(connection, username);

                List<string> tasks = new List<string>();
                using (SQLiteCommand command = new SQLiteCommand($"SELECT task FROM {TasksTable} WHERE userId=@userId", connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    using (SQLiteDataReader reader = (SQLiteDataReader)await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            tasks.Add(reader.GetString(0));
                        }
                    }
                }

                return tasks;
            }
        }

        private async Task AddTask(string username, string task)
        {
            using (SQLiteConnection connection = new SQLiteConnection($"Data Source={DatabaseFile};Version=3;"))
            {
                await connection.OpenAsync();
                long userId = await GetUserId(connection, username);

                using (SQLiteCommand command = new SQLiteCommand($"INSERT INTO {TasksTable} (userId, task) VALUES (@userId, @task)", connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@task", task);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task<long> GetUserId(SQLiteConnection connection, string username)
        {
            using (SQLiteCommand command = new SQLiteCommand($"SELECT id FROM {UsersTable} WHERE username=@username", connection))
            {
                command.Parameters.AddWithValue("@username", username);
                object result = await command.ExecuteScalarAsync();
                return Convert.ToInt64(result);
            }
        }

        private string Serialize<T>(T obj)
        {
            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                 formatter.Serialize(stream, obj);
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        
    }

}
