using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IpCruncher
{
    class Program
    {
        const uint globalStartAddress = 0x0;
        const uint globalEndAddress = globalStartAddress + 0xffff;
        static readonly int coreCount = Environment.ProcessorCount;
        static void Main(string[] args)
        {
            RunTests();
            RunSingleThreadBatch(globalStartAddress, globalEndAddress);
            RunMultithreadGlobalBatch(doLog: false);
            var _ = Console.ReadLine();
        }
        static void RunTests()
        {
            var uniques = new List<string>()
            {
                "0000",
                "124163241225",
                "65121242192",
                "16384255249",
                "21216324825",
                "0121242192",
                "063255249",
                "021863241",
                "022512420",
                "0192163255",
                "36494967",
                "248636485"
            };
            var multiples = new List<string>()
            {
                "11111",
                "12212112",
                "12481632",
                "11235813",
                "23571113",
                "14916253",
                "12345678",
                "7654321"
            };
            uniques.ForEach(s => { if (!IsUnique(s.ToIpDigits())) Console.WriteLine($"FAIL: {s} is unique but was deemed multiple"); else Console.WriteLine($"SUCCESS: {s} is unique"); });
            multiples.ForEach(s => { if (IsUnique(s.ToIpDigits())) Console.WriteLine($"FAIL: {s} is multiple but was deemed unique"); else Console.WriteLine($"SUCCESS: {s} is multiple"); });
        }
        static void RunSingleThreadBatch(uint start, uint end)
        {
            Console.WriteLine($"Starting: From {start.ToString("x8")} to {end.ToString("x8")}...");

            ulong count = end - start + 1;
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var uniqueCount = CountUniquesInBatch(start, end);
            stopwatch.Stop();
            Console.WriteLine($"Finished. Uniques: {uniqueCount}/{count} ({(100 * (double)uniqueCount / (double)count).ToString("0.00")}%). Time: {stopwatch.ElapsedMilliseconds} ms.");

        }
        static async void RunMultithreadGlobalBatch(bool doLog = false)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var tasks = new List<Task>();
            var uniquesSubtotals = new ConcurrentBag<uint>();
            var streamWriters = new ConcurrentQueue<StreamWriter>();

            for (int core = 0; core < coreCount; core++)
            {
                object core_ = core;
                var coreTask = Task.Factory.StartNew(new Action<object>((o) =>
                {
                    var batch = GetBatchForCore((int)o);
                    Console.WriteLine($"Starting core {(int)o}: From {batch.Item1.ToString("x8")} to {batch.Item2.ToString("x8")}...");
                    StreamWriter coreStreamWriter = null;
                    if (doLog)
                        coreStreamWriter = new StreamWriter($"unique{((int)o).ToString("00")}.txt") { AutoFlush = false };
                        streamWriters.Enqueue(coreStreamWriter);
                    var coreUniquesCount = CountUniquesInBatch(batch.Item1, batch.Item2, doLog ? coreStreamWriter : null);
                    uniquesSubtotals.Add(coreUniquesCount);
                }), core_);
                tasks.Add(coreTask);
            }
            try
            {
                await Task.WhenAll(tasks.ToArray());

                uint totalUniquesCount = 0;
                foreach (uint uniquesCount in uniquesSubtotals)
                    totalUniquesCount += uniquesCount;
                foreach (Task t in tasks)
                    t.Dispose();
                if (doLog)
                    foreach (var streamWriter in streamWriters)
                    {
                        streamWriter.Flush();
                        streamWriter.Close();
                        streamWriter.Dispose();
                    }
                stopwatch.Stop();
                ulong count = globalEndAddress - globalStartAddress+1;
                Console.WriteLine($"Finished. Uniques: {totalUniquesCount}/{count} ({(100 * (double)totalUniquesCount / (double)count).ToString("0.00")}%). Time: {stopwatch.ElapsedMilliseconds} ms.");
            }
            catch
            {
                throw;
            }
        }
        static (uint, uint) GetBatchForCore(int core)
        {
            uint coreStartAddress = (uint)(globalStartAddress + (globalEndAddress - globalStartAddress + 1) / (ulong)coreCount * (ulong)core);
            uint coreEndAddress = (uint)(coreStartAddress + (globalEndAddress - globalStartAddress) / (ulong)coreCount);
            return (coreStartAddress, coreEndAddress);
        }
        static uint CountUniquesInBatch(uint start, uint end, StreamWriter stream = null)
        {
            uint uniqueCount = 0;
            for (uint n = start; n <= end; n++)
                if (IsUnique(n.ToIpDigits()))
                {
                    uniqueCount++;
                    if (stream != null)
                        stream.WriteLine(n.ToIpDigits().ToIpString());
                }
            return uniqueCount;
        }
        static bool IsUnique(byte[] digits)
        {
            if (IsUniqueByHeuristics(digits)) return true;
            uint count = 0;
            foreach (var partition in EnumeratePartitions(digits.GetIpLength()))
                if (AllIpPartsValid(digits, partition))
                    count++;
            if (count > 1) return false;
            if (count == 1) return true;
            throw new Exception("Failed to parse address");
        }
        static bool IsUniqueByHeuristics(byte[] digits)
        {
            if (digits[11] != 0xff) return true; // 12 digits
            if (digits[4] == 0xff) return true; // 4 digits
            if (digits[11] == 0xff && digits[10] != 0xff) // 11 digits
            {
                if (digits[2] * 100 + digits[1] * 10 + digits[0] >= 256) return true; // that end in 256 or more
                if (digits[10] * 100 + digits[9] * 10 + digits[8] >= 256) return true; // that start with 256 or more
            }
            return false;
        }
        static IEnumerable<byte[]> EnumeratePartitions(int size)
        {
            for (byte i = 1; i < 4; i++)
                for (byte j = 1; j < 4; j++)
                    for (byte k = 1; k < 4; k++)
                        if (0 < size - i - j - k && size - i - j - k < 4) yield return new byte[] { i, j, k, (byte)(size - i - j - k) };
        }
        static int GetValidPartitionCount(byte[] digits)
        {
            int count = 0;
            foreach (var partition in EnumeratePartitions(digits.GetIpLength()))
            {
                if (AllIpPartsValid(digits, partition))
                {
                    count++;
                }
            }
            return count;
        }
        static bool AllIpPartsValid(byte[] digits, byte[] partition)
        {
            int d = 0;
            for (int b = 0; b < partition.Length; b++)
            {
                int factor = 1;
                int sum = 0;
                if (digits[d + partition[b] - 1] == 0 && partition[b] > 1) return false; // part has > 1 digits and starts with zero
                for (int i = 0; i < partition[b]; i++)
                {
                    sum += digits[d + i] * factor;
                    factor *= 10;
                }
                if (sum > 0xff) return false;
                d += partition[b];
            }
            return true;
        }
    }
    public static class Extensions
    {
        public static string ToIpString(this byte[] digits)
        {
            return string.Join("", digits.Reverse().Where(d => d != 0xff));
        }
        public static byte[] ToIpDigits(this string value)
        {
            byte[] digits = new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
            var array = value.ToCharArray().ToList();
            array.Reverse();
            for (int i = 0; i < array.Count; i++)
            {
                digits[i] = byte.Parse(array[i].ToString());
            }
            return digits;
        }
        public static byte[] ToIpDigits(this uint value)
        {
            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bytes[i] = i < 3 ? (byte)(value / (1 << i * 8) % (0x1000000 >> i * 8)) : (byte)(value / (1 << i * 8));
            }
            byte[] digits = new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff };
            int j = 0;
            for (int i = 0; i < 4; i++)
            {
                digits[j++] = (byte)(bytes[i] / 1 % 10);
                digits[j++] = (byte)(bytes[i] / 10 % 10);
                digits[j++] = (byte)(bytes[i] / 100);
                if (digits[j - 1] == 0) digits[--j] = 0xff;
                if (digits[j - 1] == 0) digits[--j] = 0xff;
            }
            return digits;
        }
        public static int GetIpLength(this byte[] digits)
        {
            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] == 0xff) return i;
            }
            return digits.Length;
        }

    }
}
