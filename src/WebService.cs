// ---------------------------------------------------------------------------
//  Встроенный сервис для веб-версии.
//
//  Настольная программа поднимает небольшой HTTP-сервис на выбранном порту,
//  и страница «Заявка на расчет.html» работает с теми же данными, что и программа:
//  прайс-листом, наборами услуг, реквизитами и заявкой. Браузер не может
//  подключиться к базе данных напрямую, поэтому посредником выступает
//  сама программа.
//
//  Обмен — обычный JSON, список адресов:
//    GET  /api/health        — доступность сервиса
//    GET  /api/data          — прайс, наборы, реквизиты, тема, отпечаток
//    POST /api/prices        — сохранить прайс-лист
//    POST /api/templates     — сохранить наборы услуг
//    POST /api/settings      — сохранить реквизиты, тему, логотип
//    GET  /api/state         — заявока: отметки, количества, цены
//    POST /api/state         — сохранить заявку
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace KotovCalc
{
    /// <summary>Хранилище заявок (отметки и количества) — общее для всех клиентов.</summary>
    internal interface IEstimateStore
    {
        /// <summary>Заявка на расчет в виде словаря для передачи по сети.</summary>
        IDictionary<string, object> LoadState();

        void SaveState(IDictionary<string, object> state);
    }

    /// <summary>Сервис для веб-версии.</summary>
    internal sealed class WebService : IDisposable
    {
        private readonly IDataStore _store;
        private readonly IEstimateStore _estimates;
        private readonly int _port;

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private string _lastError;

        public WebService(IDataStore store, IEstimateStore estimates, int port)
        {
            _store = store;
            _estimates = estimates;
            _port = port;
        }

        /// <summary>Адрес, который нужно открыть в браузере.</summary>
        public string Address
        {
            get { return "http://127.0.0.1:" + _port.ToString(CultureInfo.InvariantCulture) + "/"; }
        }

        public string LastError { get { return _lastError; } }

        public bool IsRunning { get { return _running; } }

        /// <summary>Запуск сервиса. Возвращает null при успехе или текст ошибки.</summary>
        public string Start()
        {
            if (_running) return null;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(Address);
                _listener.Start();
            }
            catch (Exception ex)
            {
                _lastError = "Не удалось открыть порт " + _port + ": " + ex.Message +
                             ". Возможно, порт занят другой программой.";
                return _lastError;
            }

            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "raschet-smeta-web";
            _thread.Start();

            _lastError = null;
            return null;
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext context = null;

                try
                {
                    context = _listener.GetContext();
                }
                catch
                {
                    if (!_running) return;
                    Thread.Sleep(200);
                    continue;
                }

                try
                {
                    Handle(context);
                }
                catch (Exception ex)
                {
                    try { Reply(context, 500, Error(ex.Message)); } catch { }
                }
                finally
                {
                    try { context.Response.Close(); } catch { }
                }
            }
        }

        // ------------------------------------------------------- обработка

        private void Handle(HttpListenerContext context)
        {
            // страница открыта как файл, поэтому разрешаем запросы с любого источника
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";

            string path = context.Request.Url.AbsolutePath.ToLowerInvariant();

            if (context.Request.HttpMethod == "OPTIONS")
            {
                Reply(context, 204, "");
                return;
            }

            if (path == "/" || path == "/api/health")
            {
                SortedDictionary<string, object> health = new SortedDictionary<string, object>(StringComparer.Ordinal);
                health["ok"] = true;
                health["store"] = _store.Title;
                health["stamp"] = SafeStamp();
                Reply(context, 200, SimpleJson.Write(health));
                return;
            }

            if (path == "/api/data" && context.Request.HttpMethod == "GET")
            {
                Reply(context, 200, SimpleJson.Write(BuildData()));
                return;
            }

            if (path == "/api/state" && context.Request.HttpMethod == "GET")
            {
                IDictionary<string, object> state = _estimates.LoadState();
                if (state == null) state = new SortedDictionary<string, object>(StringComparer.Ordinal);
                Reply(context, 200, SimpleJson.Write(state));
                return;
            }

            if (context.Request.HttpMethod == "POST")
            {
                string body = ReadBody(context);
                IDictionary<string, object> data = SimpleJson.Parse(body) as IDictionary<string, object>;

                if (data == null)
                {
                    Reply(context, 400, Error("Не удалось разобрать данные запроса."));
                    return;
                }

                if (path == "/api/prices") SavePrices(data);
                else if (path == "/api/templates") SaveTemplates(data);
                else if (path == "/api/settings") SaveSettings(data);
                else if (path == "/api/state") SaveState(data);
                else
                {
                    Reply(context, 404, Error("Неизвестный адрес: " + path));
                    return;
                }

                SortedDictionary<string, object> answer = new SortedDictionary<string, object>(StringComparer.Ordinal);
                answer["ok"] = true;
                answer["stamp"] = SafeStamp();
                Reply(context, 200, SimpleJson.Write(answer));
                return;
            }

            Reply(context, 404, Error("Неизвестный адрес: " + path));
        }

        // ------------------------------------------------ сбор и запись данных

        private IDictionary<string, object> BuildData()
        {
            StoreSnapshot snapshot = _store.LoadAll();

            SortedDictionary<string, object> result = new SortedDictionary<string, object>(StringComparer.Ordinal);
            result["ok"] = true;
            result["store"] = _store.Title;
            result["stamp"] = SafeStamp();

            List<object> prices = new List<object>();
            foreach (ServiceItem item in snapshot.Prices)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["group"] = item.Group ?? "";
                map["article"] = item.Article ?? "";
                map["name"] = item.Name ?? "";
                map["unit"] = item.Unit ?? "";
                map["price"] = item.Price;
                prices.Add(map);
            }
            result["prices"] = prices;

            List<object> templates = new List<object>();
            foreach (ServiceTemplate template in snapshot.Templates)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["name"] = template.Name ?? "";

                List<object> items = new List<object>();
                foreach (TemplateItem item in template.Items)
                {
                    SortedDictionary<string, object> entry = new SortedDictionary<string, object>(StringComparer.Ordinal);
                    entry["group"] = item.Group ?? "";
                    entry["article"] = item.Article ?? "";
                    entry["name"] = item.Name ?? "";
                    entry["quantity"] = item.Quantity;
                    items.Add(entry);
                }
                map["items"] = items;
                templates.Add(map);
            }
            result["templates"] = templates;

            SortedDictionary<string, object> document = new SortedDictionary<string, object>(StringComparer.Ordinal);
            document["number"] = snapshot.Document.Number ?? "";
            document["customer"] = snapshot.Document.Customer ?? "";
            document["discount"] = snapshot.Document.Discount;
            result["document"] = document;

            result["theme"] = snapshot.Theme ?? "light";
            result["logo"] = snapshot.Logo ?? "";

            IDictionary<string, object> state = _estimates.LoadState();
            if (state != null) result["state"] = state;

            return result;
        }

        private void SavePrices(IDictionary<string, object> data)
        {
            List<ServiceItem> prices = new List<ServiceItem>();

            foreach (object entry in SimpleJson.Array(data, "prices"))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                ServiceItem item = new ServiceItem();
                item.Group = SimpleJson.Text(map, "group");
                item.Article = SimpleJson.Text(map, "article");
                item.Name = SimpleJson.Text(map, "name");
                item.Unit = SimpleJson.Text(map, "unit");
                item.Price = SimpleJson.Number(map, "price", 0m);

                if (item.Group.Length == 0) item.Group = "Прочее";
                if (item.Unit.Length == 0) item.Unit = Uom.List[0];
                if (item.Name.Length == 0) continue;

                prices.Add(item);
            }

            _store.SavePrices(prices);
        }

        private void SaveTemplates(IDictionary<string, object> data)
        {
            List<ServiceTemplate> templates = new List<ServiceTemplate>();

            foreach (object entry in SimpleJson.Array(data, "templates"))
            {
                IDictionary<string, object> map = entry as IDictionary<string, object>;
                if (map == null) continue;

                ServiceTemplate template = new ServiceTemplate();
                template.Name = SimpleJson.Text(map, "name");
                if (template.Name.Length == 0) continue;

                foreach (object itemEntry in SimpleJson.Array(map, "items"))
                {
                    IDictionary<string, object> itemMap = itemEntry as IDictionary<string, object>;
                    if (itemMap == null) continue;

                    TemplateItem item = new TemplateItem();
                    item.Group = SimpleJson.Text(itemMap, "group");
                    item.Article = SimpleJson.Text(itemMap, "article");
                    item.Name = SimpleJson.Text(itemMap, "name");
                    item.Quantity = SimpleJson.Number(itemMap, "quantity", 1m);
                    if (item.Quantity <= 0m) item.Quantity = 1m;
                    if (item.Name.Length == 0) continue;

                    template.Items.Add(item);
                }

                if (template.Items.Count > 0) templates.Add(template);
            }

            _store.SaveTemplates(templates);
        }

        private void SaveSettings(IDictionary<string, object> data)
        {
            StoreSnapshot snapshot = _store.LoadAll();

            IDictionary<string, object> document = SimpleJson.Object(data, "document");
            if (document != null)
            {
                snapshot.Document.Number = SimpleJson.Text(document, "number");
                snapshot.Document.Customer = SimpleJson.Text(document, "customer");
                snapshot.Document.Discount = AppSettings.ClampDiscount(SimpleJson.Number(document, "discount", 0m));
                snapshot.Document.Normalize();
            }

            string theme = SimpleJson.Text(data, "theme");
            if (theme.Length > 0) snapshot.Theme = theme == "dark" ? "dark" : "light";

            if (data.ContainsKey("logo")) snapshot.Logo = SimpleJson.Text(data, "logo");

            _store.SaveSettings(snapshot);
        }

        private void SaveState(IDictionary<string, object> data)
        {
            _estimates.SaveState(data);
        }

        private string SafeStamp()
        {
            try { return _store.Stamp(); }
            catch (Exception ex) { return "ошибка: " + ex.Message; }
        }

        // -------------------------------------------------------- помощники

        private static string ReadBody(HttpListenerContext context)
        {
            using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string Error(string message)
        {
            SortedDictionary<string, object> result = new SortedDictionary<string, object>(StringComparer.Ordinal);
            result["ok"] = false;
            result["error"] = message ?? "Неизвестная ошибка.";
            return SimpleJson.Write(result);
        }

        private static void Reply(HttpListenerContext context, int code, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");

            context.Response.StatusCode = code;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        public void Dispose()
        {
            _running = false;

            try { if (_listener != null) _listener.Stop(); } catch { }
            try { if (_listener != null) _listener.Close(); } catch { }

            _listener = null;
        }
    }
}
