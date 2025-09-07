using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OracleBot : Robot
    {
        // --- НАСТРОЙКИ ---
        [Parameter("Bot ID", DefaultValue = "987654")]
        public string BotId { get; set; }

        [Parameter("Интервал (сек)", DefaultValue = 10)]
        public int HeartbeatIntervalSeconds { get; set; }

        // !!! ВАЖНО: ЗАМЕНИТЕ ЭТОТ URL НА РЕАЛЬНЫЙ URL ВАШЕЙ CLOUD FUNCTION !!!
        private const string ApiGatewayUrl = "ВАШ_URL_API_ШЛЮЗА_СЮДА";

        // API-ключ для аутентификации
        private const string ApiKey = "AIzaSyDjF2j9D8a92PxTQP_kLt325NsyQbqu5oY";

        private HttpClient _httpClient;
        private Timer _timer;

        protected override void OnStart()
        {
            Print("Запуск OracleBot...");

            // Проверяем, что URL был изменен
            if (ApiGatewayUrl.Contains("ВАШ_URL_API_ШЛЮЗА_СЮДА"))
            {
                Print("ОШИБКА: Пожалуйста, укажите реальный URL для ApiGatewayUrl в коде робота.");
                Stop();
                return;
            }

            // Инициализируем HttpClient один раз для эффективности
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", ApiKey);

            // Запускаем таймер, который будет отправлять heartbeat-сообщения
            _timer = Timer.Run(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), SendHeartbeat);

            Print("OracleBot запущен. Отправка heartbeat-сообщений каждые {0} секунд.", HeartbeatIntervalSeconds);
        }

        private async void SendHeartbeat()
        {
            try
            {
                // Формируем JSON-сообщение
                var timestamp = DateTime.UtcNow.ToString("o"); // Формат ISO 8601
                var payload = new
                {
                    bot_id = BotId,
                    status = "alive",
                    timestamp = timestamp
                };
                var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Отправляем POST-запрос
                HttpResponseMessage response = await _httpClient.PostAsync(ApiGatewayUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    Print("Heartbeat успешно отправлен. Статус: {0}", response.StatusCode);
                }
                else
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    Print("Ошибка при отправке heartbeat. Статус: {0}. Ответ: {1}", response.StatusCode, responseContent);
                }
            }
            catch (Exception ex)
            {
                Print("Критическая ошибка при отправке heartbeat: {0}", ex.Message);
            }
        }

        protected override void OnStop()
        {
            // Освобождаем ресурсы при остановке робота
            _timer?.Stop();
            _httpClient?.Dispose();
            Print("OracleBot остановлен.");
        }
    }
}
