using System;
using System.Diagnostics;
using System.Threading;

namespace MatMulLab;

public sealed class Matrix
{
    public readonly int Rows;
    public readonly int Cols;
    public readonly long[] Data;

    public Matrix(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        Data = new long[checked(rows * cols)];
    }

    public long this[int i, int j]
    {
        get => Data[i * Cols + j];
        set => Data[i * Cols + j] = value;
    }

    public long Checksum()
    {
        long sum = 0;
        for (int i = 0; i < Data.Length; i++) sum += Data[i];
        return sum;
    }
}

public sealed class LowerTriangular
{
    public readonly int N;
    public readonly long[][] RowsData;

    public LowerTriangular(int n)
    {
        N = n;
        RowsData = new long[n][];
        for (int i = 0; i < n; i++) RowsData[i] = new long[i + 1];
    }
}

public sealed class ProgressCounter
{
    private long _done = 0;
    private readonly int _total;
    private int _lastPercent = -1;

    public ProgressCounter(int total) => _total = total;

    public void Increment()
    {
        long done = Interlocked.Increment(ref _done);
        int percent = (int)(done * 100 / _total);
        
        lock (this)
        {
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                Console.WriteLine($"Прогресс: {percent}%");
            }
        }
    }
}

public static class MatrixMath
{
    public static (int start, int end)[] SplitRows(int n, int t)
    {
        var res = new (int, int)[t];
        int baseCnt = n / t, rem = n % t, cur = 0;
        for (int i = 0; i < t; i++)
        {
            int cnt = baseCnt + (i < rem ? 1 : 0);
            res[i] = (cur, cur + cnt);
            cur += cnt;
        }
        return res;
    }

    // 1. Обычное умножение 
    public static Matrix Multiply(Matrix a, Matrix b, int threads)
    {
        int n = a.Rows, k = b.Cols;
        var c = new Matrix(n, k);
        var blocks = SplitRows(n, threads);
        var progress = new ProgressCounter(n);
        var ths = new Thread[threads];

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < threads; i++)
        {
            int s = blocks[i].start, e = blocks[i].end;
            ths[i] = new Thread(() => {
                int m = a.Cols;
                for (int row = s; row < e; row++) {
                    for (int t = 0; t < m; t++) {
                        long ait = a[row, t];
                        if (ait == 0) continue;
                        int cRow = row * k, bRow = t * k;
                        for (int j = 0; j < k; j++) 
                            c.Data[cRow + j] += ait * b.Data[bRow + j];
                    }
                    progress.Increment();
                }
            });
            ths[i].Start();
        }
        foreach (var th in ths) th.Join();
        sw.Stop();

        Console.WriteLine($"Время (static): {sw.Elapsed.TotalSeconds:F3} с");
        Console.WriteLine($"Контрольная сумма: {c.Checksum()}");
        return c;
    }

    // 2. Треугольное: Статическое распределение 
    public static void MultiplyTriStatic(LowerTriangular a, Matrix b, Matrix c, int threads, ProgressCounter progress)
    {
        int n = a.N, k = b.Cols;
        var blocks = SplitRows(n, threads);
        var ths = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            int s = blocks[i].start, e = blocks[i].end;
            ths[i] = new Thread(() => {
                for (int i = s; i < e; i++) {
                    var aRow = a.RowsData[i];
                    int cRow = i * k;
                    for (int t = 0; t < aRow.Length; t++) {
                        long ait = aRow[t];
                        int bRow = t * k;
                        for (int j = 0; j < k; j++) c.Data[cRow + j] += ait * b.Data[bRow + j];
                    }
                    progress.Increment();
                }
            });
            ths[i].Start();
        }
        foreach (var th in ths) th.Join();
    }

    // 3. Треугольное: Динамическое распределение
    public static void MultiplyTriDynamic(LowerTriangular a, Matrix b, Matrix c, int threads, ProgressCounter progress)
    {
        int n = a.N, k = b.Cols;
        int nextRow = -1;
        var ths = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            ths[i] = new Thread(() => {
                while (true) {
                    int i = Interlocked.Increment(ref nextRow);
                    if (i >= n) break;
                    var aRow = a.RowsData[i];
                    int cRow = i * k;
                    for (int t = 0; t < aRow.Length; t++) {
                        long ait = aRow[t];
                        int bRow = t * k;
                        for (int j = 0; j < k; j++) c.Data[cRow + j] += ait * b.Data[bRow + j];
                    }
                    progress.Increment();
                }
            });
            ths[i].Start();
        }
        foreach (var th in ths) th.Join();
    }
}