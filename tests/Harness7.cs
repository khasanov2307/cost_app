// Проверки встроенного сервиса для веб-версии: поднимаем и обращаемся по сети.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using KotovCalc;

internal static class Harness7
{
    private static int _failed;

    private static void Check(string what, object expected, object actual)
    {
        bool ok = Equals(expected, actual);
        if (!ok) _failed++;
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what +
            (ok ? "" : "   ожидалось=<" + expected + "> получено=<" + actual + ">"));
    }

    private static void CheckTrue(string what, bool value, string details)
    {
        if (!value) _failed++;
        Console.WriteLine((value ? "  PASS  " : "  FAIL  ") + what + (value ? "" : "   " + details));
    }

    private static int _step;

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("[" + (++_step) + "] " + title);
    }

    /// <summary>Смета в памяти — для проверки обмена.</summary>
    private sealed class TestEstimate : IEstimateStore
    {
        public SortedDictionary<string, object> State = new SortedDictionary<string, object>(StringComparer.Ordinal);

        public IDictionary<string, object> LoadState()
        {
            return State;
        }

        public void SaveState(IDictionary<string, object> state)
        {
            State = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in state) State[pair.Key] = pair.Value;
        }
    }

    private static string Get(string url)
    {
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Timeout = 10000;
        request.Method = "GET";

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            return reader.ReadToEnd();
    }

    private static string Post(string url, string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        request.Timeout = 10000;
        request.Method = "POST";
        request.ContentType = "application/json; charset=utf-8";
        request.ContentLength = bytes.Length;

        using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);

        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            return reader.ReadToEnd();
    }

    private static void Main(string[] args)
    {
        int port = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 8791;
        string work = Path.Combine(Path.GetTempPath(), "kotov-web-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(work);
        PriceBook.StorePath = Path.Combine(work, "prices.xml");

        try
        {
            Section("Запуск сервиса");

            IDataStore store = new FileDataStore();
            store.Prepare();

            TestEstimate estimate = new TestEstimate();
            WebService service = new WebService(store, estimate, port);

            string error = service.Start();
            CheckTrue("сервис запущен", error == null, error);
            CheckTrue("адрес указан", service.Address.Contains(port.ToString(CultureInfo.InvariantCulture)), service.Address);
            Console.WriteLine("        адрес: " + service.Address);

            // второй сервис на том же порту должен сообщить о занятости
            WebService second = new WebService(store, estimate, port);
            string busy = second.Start();
            CheckTrue("занятый порт распознан", busy != null, "ошибки нет");
            second.Dispose();

            Section("Проверка доступности");

            string health = Get(service.Address + "api/health");
            IDictionary<string, object> healthData = SimpleJson.Parse(health) as IDictionary<string, object>;
            CheckTrue("ответ разобран", healthData != null, health);
            CheckTrue("сервис отвечает согласием", Convert.ToString(healthData["ok"]) == "True", health);
            CheckTrue("режим хранилища назван", SimpleJson.Text(healthData, "store").Length > 5,
                SimpleJson.Text(healthData, "store"));
            CheckTrue("отпечаток есть", SimpleJson.Text(healthData, "stamp").Length > 0, "пусто");

            Section("Чтение данных");

            string data = Get(service.Address + "api/data");
            IDictionary<string, object> parsed = SimpleJson.Parse(data) as IDictionary<string, object>;
            CheckTrue("данные разобраны", parsed != null, data);
            CheckTrue("прайс передан", SimpleJson.Array(parsed, "prices").Count > 30,
                "позиций: " + SimpleJson.Array(parsed, "prices").Count);

            Section("Сохранение прайса через сервис");

            string pricesJson = "{\"prices\":[" +
                "{\"group\":\"Раздел А\",\"article\":\"ART-1\",\"name\":\"Услуга с кавычкой \\\" и кириллицей\",\"unit\":\"услуга\",\"price\":1234.56}," +
                "{\"group\":\"Раздел Б\",\"article\":\"ART-2\",\"name\":\"Вторая услуга\",\"unit\":\"шт.\",\"price\":10}]}";

            string saved = Post(service.Address + "api/prices", pricesJson);
            IDictionary<string, object> savedData = SimpleJson.Parse(saved) as IDictionary<string, object>;
            CheckTrue("прайс сохранён", Convert.ToString(savedData["ok"]) == "True", saved);

            List<ServiceItem> fromStore = store.LoadPrices();
            Check("позиций в хранилище", 2, fromStore.Count);
            Check("кириллица и кавычка сохранены", "Услуга с кавычкой \" и кириллицей", fromStore[0].Name);
            Check("цена сохранена", 1234.56m, fromStore[0].Price);
            Check("раздел сохранён", "Раздел Б", fromStore[1].Group);
            Check("порядок сохранён", "ART-1", fromStore[0].Article);

            Section("Сохранение наборов через сервис");

            string templatesJson = "{\"templates\":[{\"name\":\"ТО-1\",\"items\":[{\"group\":\"Раздел А\"," +
                                   "\"article\":\"ART-1\",\"name\":\"Услуга с кавычкой \\\" и кириллицей\",\"quantity\":3}]}]}";
            saved = Post(service.Address + "api/templates", templatesJson);
            savedData = SimpleJson.Parse(saved) as IDictionary<string, object>;
            CheckTrue("наборы сохранены", Convert.ToString(savedData["ok"]) == "True", saved);

            List<ServiceTemplate> templates = store.LoadTemplates();
            Check("наборов в хранилище", 1, templates.Count);
            Check("имя набора", "ТО-1", templates[0].Name);
            Check("количество из набора", 3m, templates[0].Items[0].Quantity);

            Section("Сохрание реквизитов через сервис");

            string settingsJson = "{\"document\":{\"number\":\"9/2026\",\"customer\":\"Петров Пётр\",\"discount\":7.5}," +
                                  "\"theme\":\"dark\",\"logo\":\"data:image/png;base64,AAAA\"}";
            saved = Post(service.Address + "api/settings", settingsJson);
            savedData = SimpleJson.Parse(saved) as IDictionary<string, object>;
            CheckTrue("реквизиты сохранены", Convert.ToString(savedData["ok"]) == "True", saved);

            StoreSnapshot snapshot = store.LoadAll();
            Check("номер сметы сохранён", "9/2026", snapshot.Document.Number);
            Check("заказчик сохранён", "Петров Пётр", snapshot.Document.Customer);
            Check("скидка сохранена", 7.5m, snapshot.Document.Discount);
            Check("тема сохранена", "dark", snapshot.Theme);
            Check("логотип сохранён", "data:image/png;base64,AAAA", snapshot.Logo);

            Section("Смета: отметки и количества");

            string stateJson = "{\"chosen\":{\"ключ\":{\"quantity\":2,\"price\":900}},\"collapsing\":{}}";
            saved = Post(service.Address + "api/state", stateJson);
            IDictionary<string, object> stateSaved = SimpleJson.Parse(saved) as IDictionary<string, object>;
            CheckTrue("смета сохранена", stateSaved != null && Convert.ToString(stateSaved["ok"]) == "True", saved);

            string stateBack = Get(service.Address + "api/state");
            IDictionary<string, object> stateData = SimpleJson.Parse(stateBack) as IDictionary<string, object>;
            IDictionary<string, object> chosen = SimpleJson.Object(stateData, "chosen");
            CheckTrue("отметки переданы обратно", chosen != null && chosen.Count == 1, stateBack);

            // смета попадает и в общий ответ данных
            data = Get(service.Address + "api/data");
            parsed = SimpleJson.Parse(data) as IDictionary<string, object>;
            CheckTrue("смета внутри общих данных", SimpleJson.Object(parsed, "state") != null, data);

            Section("Ошибочные запросы");

            bool refused = false;
            try { Get(service.Address + "api/нет-такого"); }
            catch (WebException ex) { refused = ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.NotFound; }
            CheckTrue("неизвестный адрес отклонён", refused, "запрос прошёл");

            refused = false;
            try { Post(service.Address + "api/prices", "это не json"); }
            catch (WebException ex) { refused = ((HttpWebResponse)ex.Response).StatusCode == HttpStatusCode.BadRequest; }
            CheckTrue("испорченные данные отклонены", refused, "запрос прошёл");

            Section("Важность доступности базы");

            IDataStore dead = new SqlDataStore(new PgConnectionInfo("127.0.0.1", 5999, "smeta", "smeta", "smeta"));
            WebService deadService = new WebService(dead, estimate, port + 1);
            string deadError = deadService.Start();
            CheckTrue("сервис поднялся над недоступной базой", deadError == null, deadError);

            if (deadError == null)
            {
                refused = false;
                try { Get(deadService.Address + "api/data"); }
                catch (WebException) { refused = true; }
                CheckTrue("ошибка базы сообщается клиенту", refused, "запрос прошёл");
                deadService.Dispose();
            }

            Section("Останов сервиса");

            service.Dispose();
            refused = false;
            try { Get(service.Address + "api/health"); }
            catch (WebException) { refused = true; }
            CheckTrue("после останова сервис не отвечает", refused, "сервис отвечает");

            Console.WriteLine();
            if (_failed == 0) { Console.WriteLine("ВСЕ ПРОВЕРКИ СЕРВИСА ПРОЙДЕНЫ"); Environment.Exit(0); }
            Console.WriteLine("ПРОВАЛЕНО: " + _failed);
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ОШИБКА: " + ex.GetType().Name + ": " + ex.Message);
            Environment.Exit(2);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }
}
