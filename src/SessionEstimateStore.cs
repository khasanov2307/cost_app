// ---------------------------------------------------------------------------
//  Смета (отметки, количества и цены) для веб-версии.
//
//  Хранится рядом с данными программы отдельным файлом, чтобы страница
//  и настольная программа работали с одной и той же сметой.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KotovCalc
{
    internal sealed class SessionEstimateStore : IEstimateStore
    {
        private const string FileName = "estimate-state.json";

        private static string Path
        {
            get { return System.IO.Path.Combine(PriceBook.StoreFolder, FileName); }
        }

        public IDictionary<string, object> LoadState()
        {
            try
            {
                if (!File.Exists(Path)) return new SortedDictionary<string, object>(StringComparer.Ordinal);

                string text = File.ReadAllText(Path, Encoding.UTF8);
                IDictionary<string, object> state = SimpleJson.Parse(text) as IDictionary<string, object>;
                return state ?? new SortedDictionary<string, object>(StringComparer.Ordinal);
            }
            catch
            {
                return new SortedDictionary<string, object>(StringComparer.Ordinal);
            }
        }

        public void SaveState(IDictionary<string, object> state)
        {
            string text = SimpleJson.Write(state ?? new SortedDictionary<string, object>(StringComparer.Ordinal));
            File.WriteAllText(Path, text, new UTF8Encoding(true));
        }
    }
}