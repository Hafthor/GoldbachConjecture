using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.IO.Compression;

namespace GoldbachConjecture;

public static class GoldbachCometGap {
    /// <summary>
    /// Wrote this in the interest of disproving my own conjecture that there exists no positive integer n that is
    /// not a Goldbach count for some even integer, where the Goldbach count of an even integer is the unique number
    /// of ways it can be expressed as a sum of two primes.
    /// For example, 0 is the Goldback count for 2 since it cannot be expressed as a sum of two primes, 1 is the
    /// Goldbach count for 4 since it can be expressed as 2+2, 2 is the Goldbach count for 10 since it can be expressed
    /// as 3+7 and 5+5, and so on.
    /// </summary>
    public static void Execute() {
        List<uint> primes = GetPrimes();
        uint rangeStart = 0, rangeIncrement = 65536, ranges = 65536;
        List<bool> found = [];
        rangeStart -= rangeIncrement; // so that the first loop iteration starts at 0
        Parallel.For(0, ranges, _ => {
            uint[] rangeEvenCounts = ArrayPool<uint>.Shared.Rent((int)(rangeIncrement >> 1));
            try {
                Array.Clear(rangeEvenCounts);
                uint rs = Interlocked.Add(ref rangeStart, rangeIncrement);
                uint re = rs + rangeIncrement - 1;
                string message = ReadRange(rs, re, rangeEvenCounts) ?? ComputeRange(primes, rs, re, rangeEvenCounts);
                (int firstMissingCount, int nextMissingCount, int missingCount) =
                    UpdateFound(found, rangeEvenCounts, 0);
                double density = 1.0 - missingCount / ((double)found.Count - firstMissingCount);
                Console.WriteLine(
                    $"{message} First missing count = {firstMissingCount:N0}, next = {nextMissingCount:N0}, highest count = {found.Count - 1:N0}, missing count = {missingCount:N0}, density = {density * 100:N2}%.");
            } finally {
                ArrayPool<uint>.Shared.Return(rangeEvenCounts);
            }
        });
        return;
        
        // returns all primes in the uint space
        static List<uint> GetPrimes() {
            Console.Write("Finding primes...");
            List<uint> primes = [2];
            // uint.MaxValue is 4,294,967,295 which is composite
            // int.MaxValue is 2,147,483,647 (x2 + 1) = 4,294,967,295
            BitArray isOddComposite = new(int.MaxValue);
            isOddComposite[int.MaxValue - 1] = true; // mark 4,294,967,293 (2^32-3) as composite
            uint sqrtLimit = 65536;
            for (uint n = 3; n < sqrtLimit; n += 2)
                if (!isOddComposite[(int)(n >> 1)]) {
                    primes.Add(n);
                    for (uint add = n << 1, comp = n + add; comp > add && comp != uint.MaxValue; comp += add)
                        isOddComposite[(int)(comp >> 1)] = true;
                }

            Console.Write(' ');
            for (uint n = sqrtLimit + 1; n < uint.MaxValue; n += 2)
                if (!isOddComposite[(int)(n >> 1)])
                    primes.Add(n);

            Console.WriteLine($"done. Found {primes.Count:N0} primes. Highest prime is {primes[^1]:N0}.");
            return primes;
        }

        static string FileNameForRange(uint rangeStart, uint rangeEnd) => $"counts-{rangeStart}-{rangeEnd}.gz";

        static string ReadRange(uint rangeStart, uint rangeEnd, uint[] rangeEvenCounts) {
            string fileName = FileNameForRange(rangeStart, rangeEnd);
            if (!File.Exists(fileName)) return null;
            using FileStream file = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using StreamReader sr = new(gzip);
            for (int i = 0; i < rangeEvenCounts.Length; i++) {
                string line = sr.ReadLine();
                if (line == null) break;
                rangeEvenCounts[i] = uint.Parse(line);
            }
            
            return $"Counts for range [{rangeStart:N0}...{rangeEnd:N0}] read from disk.";
        }

        static string ComputeRange(List<uint> primes, uint rangeStart, uint rangeEnd, uint[] rangeEvenCounts) {
            int pmax = primes.BinarySearch(rangeEnd >> 1);
            if (pmax < 0) pmax = ~pmax;
            for (int pi = 1; pi < pmax; pi++) { // skip the first prime (2)
                uint p = primes[pi];
                int qi = rangeStart > p
                    ? ~primes.BinarySearch(rangeStart - p - 1)
                    : pi; // rangeStart is even, p is odd, so we subtract 1 to guarantee that we won't find it exactly
                if (qi < pi) qi = pi;
                // rangeEnd is odd, p is odd, so we will not find it exactly
                int qmax = ~primes.BinarySearch(rangeEnd - p); 
                for (uint rsmp = rangeStart - p; qi < qmax; qi++)
                    rangeEvenCounts[(primes[qi] - rsmp) >> 1]++;
            }
            return WriteRange(rangeStart, rangeEnd, rangeEvenCounts);
        }
        
        static string WriteRange(uint rangeStart, uint rangeEnd, uint[] rangeEvenCounts) {
            using FileStream fs = new FileStream(FileNameForRange(rangeStart, rangeEnd), FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            using GZipStream gz = new(fs, CompressionMode.Compress);
            using StreamWriter sw = new(gz);
            foreach (uint evenCount in rangeEvenCounts)
                sw.WriteLine(evenCount);
            return $"Counts for range [{rangeStart:N0}...{rangeEnd:N0}] computed and saved.";
        }

        static (int, int, int) UpdateFound(List<bool> found, uint[] rangeEvenCounts, int prevFirstMissingCount) {
            lock (found) {
                foreach (uint count in rangeEvenCounts) {
                    while (count > found.Count) found.Add(false);
                    if (count < found.Count) found[(int)count] = true;
                    else found.Add(true);
                }
            }

            while (prevFirstMissingCount < found.Count && found[prevFirstMissingCount])
                prevFirstMissingCount++;
            int nextMissingCount = prevFirstMissingCount + 1;
            while (nextMissingCount < found.Count && found[nextMissingCount])
                nextMissingCount++;
            int missingCount = 1;
            for (int i = nextMissingCount; i < found.Count; i++)
                if (!found[i])
                    missingCount++;
            return (prevFirstMissingCount, nextMissingCount, missingCount);
        }
    }
}