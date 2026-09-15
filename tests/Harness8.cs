// Поднимает сервис над файловым хранилищем и держит его до нажатия клавиши.
// Нужен для проверки связки веб-страницы с программой.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KotovCalc;

internal static class Harness8
{
    private static void Main(string[] args)
    {
        int port = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 8801;

        string work = Path.Combine(Path.GetTempPath(), "kotov-service");
        if (!Directory.Exists(work)) Directory.CreateDirectory(work);
        PriceBook.StorePath = Path.Combine(work, "prices.xml");

        IDataStore store = new FileDataStore();
        store.Prepare();

        WebService service = new WebService(store, new SessionEstimateStore(), port);
        string error = service.Start();

        if (error != null)
        {
            Console.Error.WriteLine(error);
            Environment.Exit(1);
        }

        Console.WriteLine("сервис: " + service.Address + "  хранилище: " + store.Title);

        // ждём, пока родительский процесс не завершит нас
        System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);

        service.Dispose();
    }
}
