// ---------------------------------------------------------------------------
//  Заказчики и их автомобили.
//
//  Заказчика можно выбрать из справочника или ввести вручную строкой:
//  в заявке хранится и ссылка на карточку, и текст, который видел оператор.
//
//  Хранение: файл «заказчики.json» в папке данных либо таблица smeta_customers.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KotovCalc
{
    /// <summary>Карточка заказчика.</summary>
    internal sealed class Customer
    {
        public string Id = "";              // постоянный признак карточки
        public string Name = "";            // ФИО или название организации
        public string Phone = "";
        public string Car = "";             // марка и модель
        public string Plate = "";           // госномер
        public string Note = "";
        public DateTime Created = DateTime.Now;

        /// <summary>Строка для списка и подсказок.</summary>
        public string Caption
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.Append(Name.Length == 0 ? "без имени" : Name);
                if (Phone.Length > 0) text.Append("   ").Append(Phone);
                return text.ToString();
            }
        }

        /// <summary>Строка для реквизитов заявки: имя и телефон.</summary>
        public string Requisites
        {
            get
            {
                if (Phone.Length == 0) return Name;

                StringBuilder text = new StringBuilder();
                text.Append(Name);
                if (Name.Length > 0) text.Append(", ");
                text.Append(Phone);
                return text.ToString();
            }
        }

        public override string ToString() { return Caption; }
    }

    /// <summary>Справочник заказчиков.</summary>
    internal sealed class CustomerBook
    {
        public List<Customer> Customers = new List<Customer>();

        /// <summary>Карточка по признаку.</summary>
        public Customer Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (Customer customer in Customers)
                if (string.Equals(customer.Id, id, StringComparison.OrdinalIgnoreCase))
                    return customer;

            return null;
        }

        /// <summary>
        /// Поиск карточки по имени и телефону: так заявка связывается с заказчиком,
        /// даже если её завели вручную.
        /// </summary>
        public Customer Match(string name, string phone)
        {
            string cleanName = (name ?? "").Trim();
            string cleanPhone = Digits(phone);

            if (cleanName.Length == 0 && cleanPhone.Length == 0) return null;

            // сначала точное совпадение по телефону
            if (cleanPhone.Length > 0)
            {
                foreach (Customer customer in Customers)
                    if (Digits(customer.Phone) == cleanPhone)
                        return customer;
            }

            if (cleanName.Length == 0) return null;

            foreach (Customer customer in Customers)
                if (string.Equals(customer.Name.Trim(), cleanName, StringComparison.CurrentCultureIgnoreCase))
                    return customer;

            return null;
        }

        /// <summary>Поиск по части имени, телефона, машины или номера.</summary>
        public List<Customer> Search(string query)
        {
            List<Customer> found = new List<Customer>();
            string clean = (query ?? "").Trim();

            if (clean.Length == 0)
            {
                found.AddRange(Customers);
                Sort(found);
                return found;
            }

            string digits = Digits(clean);

            foreach (Customer customer in Customers)
            {
                if (Contains(customer.Name, clean) || Contains(customer.Car, clean) ||
                    Contains(customer.Plate, clean) || Contains(customer.Note, clean) ||
                    Contains(customer.Phone, clean) ||
                    (digits.Length > 1 && PhoneMatches(customer.Phone, digits)))
                    found.Add(customer);
            }

            Sort(found);
            return found;
        }

        /// <summary>
        /// Совпадение по телефону: ведущая восьмёрка или семёрка не мешает,
        /// поэтому 89037775533 находит +7 (903) 777-55-33.
        /// </summary>
        private static bool PhoneMatches(string phone, string queryDigits)
        {
            string phoneDigits = Digits(phone);

            if (phoneDigits.Length == 0 || queryDigits.Length == 0) return false;
            if (phoneDigits.IndexOf(queryDigits, StringComparison.Ordinal) >= 0) return true;

            // сравниваем без ведущей цифры выхода на междугороднюю связь
            string shortPhone = WithoutLead(phoneDigits);
            string shortQuery = WithoutLead(queryDigits);

            if (shortQuery.Length < 2) return false;
            return shortPhone.IndexOf(shortQuery, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Телефон без ведущей восьмёрки или семёрки.</summary>
        private static string WithoutLead(string digits)
        {
            if (digits.Length > 10 && (digits[0] == '8' || digits[0] == '7'))
                return digits.Substring(1);

            return digits;
        }

        private static bool Contains(string text, string query)
        {
            return text != null && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>Только цифры: телефоны сравниваются без скобок и дефисов.</summary>
        public static string Digits(string text)
        {
            StringBuilder digits = new StringBuilder();
            foreach (char symbol in text ?? "")
                if (char.IsDigit(symbol)) digits.Append(symbol);

            return digits.ToString();
        }

        private static void Sort(List<Customer> list)
        {
            list.Sort(delegate(Customer left, Customer right)
            {
                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        /// <summary>Создание карточки: если такая уже есть, возвращается она же.</summary>
        public Customer Ensure(string name, string phone)
        {
            Customer existing = Match(name, phone);
            if (existing != null)
            {
                // дополняем пустые поля, но ничего не затираем
                if (existing.Phone.Length == 0 && !string.IsNullOrEmpty(phone)) existing.Phone = phone.Trim();
                if (existing.Name.Length == 0 && !string.IsNullOrEmpty(name)) existing.Name = name.Trim();
                return existing;
            }

            Customer created = new Customer();
            created.Id = Guid.NewGuid().ToString("N");
            created.Name = (name ?? "").Trim();
            created.Phone = (phone ?? "").Trim();
            created.Created = DateTime.Now;

            Customers.Add(created);
            return created;
        }

        /// <summary>Сколько заявок у заказчика.</summary>
        public static int CountEstimates(Customer customer, IList<SavedEstimate> estimates)
        {
            if (customer == null || estimates == null) return 0;

            int count = 0;

            foreach (SavedEstimate estimate in estimates)
            {
                if (estimate.CustomerId.Length > 0 &&
                    string.Equals(estimate.CustomerId, customer.Id, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    continue;
                }

                // заявки, заведённые вручную, связываем по имени и телефону
                if (estimate.CustomerId.Length == 0 &&
                    string.Equals(estimate.Customer.Trim(), customer.Name.Trim(),
                                  StringComparison.CurrentCultureIgnoreCase) &&
                    (Digits(estimate.CustomerPhone) == Digits(customer.Phone) ||
                     customer.Phone.Length == 0 || estimate.CustomerPhone.Length == 0))
                    count++;
            }

            return count;
        }

        /// <summary>Сумма оплаченного по заказчику.</summary>
        public static decimal PaidTotal(Customer customer, IList<SavedEstimate> estimates, CashBook cash)
        {
            if (customer == null || estimates == null || cash == null) return 0m;

            decimal total = 0m;

            foreach (SavedEstimate estimate in estimates)
            {
                bool mine = estimate.CustomerId.Length > 0
                    ? string.Equals(estimate.CustomerId, customer.Id, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(estimate.Customer.Trim(), customer.Name.Trim(),
                                    StringComparison.CurrentCultureIgnoreCase);

                if (!mine) continue;

                Payment payment = cash.FindPayment(estimate.Number);
                if (payment != null) total += payment.Total;
            }

            return total;
        }
    }

    /// <summary>Запись и чтение справочника заказчиков в файле.</summary>
    internal static class CustomerJson
    {
        public const string Format = "raschet-smeta-customer";

        public static string ToJson(Customer customer)
        {
            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = Format;
            root["id"] = customer.Id;
            root["name"] = customer.Name;
            root["phone"] = customer.Phone;
            root["car"] = customer.Car;
            root["plate"] = customer.Plate;
            root["note"] = customer.Note;
            root["created"] = customer.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return SimpleJson.Write(root);
        }

        public static Customer FromJson(string json)
        {
            IDictionary<string, object> root = SimpleJson.Parse(json) as IDictionary<string, object>;
            if (root == null || SimpleJson.Text(root, "format") != Format) return null;

            Customer customer = new Customer();
            customer.Id = SimpleJson.Text(root, "id");
            customer.Name = SimpleJson.Text(root, "name");
            customer.Phone = SimpleJson.Text(root, "phone");
            customer.Car = SimpleJson.Text(root, "car");
            customer.Plate = SimpleJson.Text(root, "plate");
            customer.Note = SimpleJson.Text(root, "note");

            DateTime created;
            if (DateTime.TryParse(SimpleJson.Text(root, "created"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out created))
                customer.Created = created;

            if (customer.Id.Length == 0) customer.Id = Guid.NewGuid().ToString("N");
            if (customer.Name.Length == 0) return null;

            return customer;
        }
    }

    /// <summary>Справочник заказчиков в файлах.</summary>
    internal sealed class FileCustomerBook : ICustomerStore
    {
        public string Title { get { return "Заказчики в файлах: " + FileName; } }

        private static string FileName
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, "заказчики.json"); }
        }

        public CustomerBook Load()
        {
            CustomerBook book = new CustomerBook();
            string file = FileName;
            if (!File.Exists(file)) return book;

            try
            {
                IDictionary<string, object> root =
                    SimpleJson.Parse(File.ReadAllText(file, Encoding.UTF8)) as IDictionary<string, object>;

                if (root != null)
                {
                    foreach (object entry in SimpleJson.Array(root, "customers"))
                    {
                        IDictionary<string, object> map = entry as IDictionary<string, object>;
                        if (map == null) continue;

                        Customer customer = new Customer();
                        customer.Id = SimpleJson.Text(map, "id");
                        customer.Name = SimpleJson.Text(map, "name");
                        customer.Phone = SimpleJson.Text(map, "phone");
                        customer.Car = SimpleJson.Text(map, "car");
                        customer.Plate = SimpleJson.Text(map, "plate");
                        customer.Note = SimpleJson.Text(map, "note");

                        if (customer.Name.Length == 0) continue;
                        if (customer.Id.Length == 0) customer.Id = Guid.NewGuid().ToString("N");

                        book.Customers.Add(customer);
                    }
                }
            }
            catch { /* повреждённый файл: начнём с пустого справочника */ }

            return book;
        }

        public void Save(CustomerBook book)
        {
            if (book == null) return;

            string folder = PriceBook.StoreFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            List<object> customers = new List<object>();

            foreach (Customer customer in book.Customers)
            {
                SortedDictionary<string, object> map = new SortedDictionary<string, object>(StringComparer.Ordinal);
                map["id"] = customer.Id;
                map["name"] = customer.Name ?? "";
                map["phone"] = customer.Phone ?? "";
                map["car"] = customer.Car ?? "";
                map["plate"] = customer.Plate ?? "";
                map["note"] = customer.Note ?? "";
                customers.Add(map);
            }

            SortedDictionary<string, object> root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["format"] = "raschet-smeta-customers";
            root["customers"] = customers;

            File.WriteAllText(FileName, SimpleJson.Write(root), new UTF8Encoding(true));
        }
    }

    /// <summary>Хранилище заказчиков: файлы или база данных.</summary>
    internal interface ICustomerStore
    {
        string Title { get; }
        CustomerBook Load();
        void Save(CustomerBook book);
    }
}
