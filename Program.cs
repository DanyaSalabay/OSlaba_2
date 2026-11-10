using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace MatMulLab;

class Program
{
    static void Main(string[] args)
    {
        // Проверка: если программу запустил сам вычислитель для генерации данных
        if (args.Length > 0 && args[0] == "generate-child")
        {
            int n = int.Parse(args[1]);
            int k = int.Parse(args[2]);
            RunGenerator(n, k);
            return; // после отправки генератор завершается
        }

        Console.WriteLine("=== Лабораторная работа 2. Многопоточное умножение матриц ===");
        Console.WriteLine("1. Задание на оценку 3 (Обычное умножение)");
        Console.WriteLine("2. Задание на оценку 4 (Треугольная матрица + 2 процесса)");
        Console.Write("Выберите уровень (1 или 2): ");
        
        string choice = Console.ReadLine();

        if (choice == "1")
        {
            RunLevel3();
        }
        else if (choice == "2")
        {
            RunLevel4();
        }
    }

    static void RunLevel3()
    {
        Console.WriteLine("\n--- УРОВЕНЬ 3 ---");
        Console.Write("Введите количество строк матрицы A (n, например 2000): ");
        int n = int.Parse(Console.ReadLine());
        Console.Write("Введите количество столбцов матрицы A (m, например 1500): ");
        int m = int.Parse(Console.ReadLine());
        Console.Write("Введите количество столбцов матрицы B (k, например 1800): ");
        int k = int.Parse(Console.ReadLine());
        Console.Write("Введите количество потоков T (например, 4): ");
        int threads = int.Parse(Console.ReadLine());

        var rnd = new Random(42);
        var a = new Matrix(n, m);
        var b = new Matrix(m, k);
        
        for (int i = 0; i < n; i++) 
        for (int j = 0; j < m; j++) 
            a[i, j] = rnd.Next(0, 10);
                
        for (int i = 0; i < m; i++) 
        for (int j = 0; j < k; j++) 
            b[i, j] = rnd.Next(0, 10);

        Console.WriteLine($"Запуск умножения матриц {n}x{m} и {m}x{k} на {threads} потоках...");
        MatrixMath.Multiply(a, b, threads);
    }

    static void RunLevel4()
    {
        Console.WriteLine("\n--- УРОВЕНЬ 4 (Два процесса) ---");
        Console.Write("Введите размер треугольной матрицы n (например, 2000): ");
        int n = int.Parse(Console.ReadLine());
        Console.Write("Введите размер обычной матрицы k (например, 2000): ");
        int k = int.Parse(Console.ReadLine());
        Console.Write("Введите количество потоков T (например, 12): ");
        int threads = int.Parse(Console.ReadLine());
        Console.Write("Введите способ (static или dynamic): ");
        string method = Console.ReadLine();

        // 1. Запускаем ВТОРОЙ процесс (Генератор) через Process.Start
        Console.WriteLine("Запуск процесса-генератора...");
        string exePath = Environment.ProcessPath; // путь к текущему exe
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"generate-child {n} {k}",
            UseShellExecute = false
        };
        
        using var generatorProcess = Process.Start(startInfo);

        // 2. Сами становимся Вычислителем и ждем данные
        RunReceiver(threads, method);

        // Дожидаемся закрытия процесса-генератора
        generatorProcess?.WaitForExit();
    }

    static void RunGenerator(int n, int k)
    {
        var rnd = new Random(42);
        var a = new LowerTriangular(n);
        var b = new Matrix(n, k);
        for (int i = 0; i < n; i++) for (int j = 0; j <= i; j++) a.RowsData[i][j] = rnd.Next(0, 10);
        for (int i = 0; i < n; i++) for (int j = 0; j < k; j++) b[i, j] = rnd.Next(0, 10);

        using var pipe = new NamedPipeClientStream(".", "matmul_pipe", PipeDirection.Out);
        pipe.Connect();
        using var writer = new BinaryWriter(pipe);

        writer.Write(n);
        writer.Write(k);

        for (int i = 0; i < n; i++)
        {
            writer.Write(i + 1);
            for (int j = 0; j <= i; j++) writer.Write(a.RowsData[i][j]);
        }

        for (int i = 0; i < n * k; i++) writer.Write(b.Data[i]);
        writer.Flush();
    }

    static void RunReceiver(int threads, string method)
    {
        Console.WriteLine($"Вычислитель: жду данные из пайпа...");
        
        // Таймер ожидания данных
        var swWait = Stopwatch.StartNew(); 
        
        using var pipe = new NamedPipeServerStream("matmul_pipe", PipeDirection.In);
        pipe.WaitForConnection();
        using var reader = new BinaryReader(pipe);

        int n = reader.ReadInt32();
        int k = reader.ReadInt32();

        var a = new LowerTriangular(n);
        for (int i = 0; i < n; i++)
        {
            int len = reader.ReadInt32();
            for (int j = 0; j < len; j++) a.RowsData[i][j] = reader.ReadInt64();
        }

        var b = new Matrix(n, k);
        for (int i = 0; i < n * k; i++) b.Data[i] = reader.ReadInt64();

        // Стоп таймер
        swWait.Stop();
        Console.WriteLine($"Вычислитель: данные получены за {swWait.Elapsed.TotalSeconds:F3} с.");

        Console.WriteLine($"Запуск {method} на {threads} потоках...");

        var c = new Matrix(n, k);
        var progress = new ProgressCounter(n);
        var sw = Stopwatch.StartNew();

        if (method == "static")
            MatrixMath.MultiplyTriStatic(a, b, c, threads, progress);
        else
            MatrixMath.MultiplyTriDynamic(a, b, c, threads, progress);

        sw.Stop();
        Console.WriteLine($"Время ({method}): {sw.Elapsed.TotalSeconds:F3} с");
        Console.WriteLine($"Контрольная сумма: {c.Checksum()}");
    }
}