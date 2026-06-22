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
        const int rangeIncrementBits = 16;
        const uint rangeIncrement = 1u << rangeIncrementBits, ranges = 1u << (32 - rangeIncrementBits);
        uint[] primes = GetPrimes();
        List<bool> countsFound = [];
        uint rangeStart = unchecked(0 - rangeIncrement); // so that the first loop iteration starts at 0
        int incrementsDone = 0, maxIncrementDone = 0;

        Parallel.For(0, ranges, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, _ => {
            uint[] rangeEvenCounts = new uint[rangeIncrement >> 1];
            uint rs = Interlocked.Add(ref rangeStart, rangeIncrement), re = rs + rangeIncrement - 1;
            string message = ReadRange(rs, re, rangeEvenCounts) ??
                             ComputeRange(primes, rs, re, rangeEvenCounts) ??
                             WriteRange(rs, re, rangeEvenCounts);

            bool incompleteRange;
            int firstMissingCount, nextMissingCount, missingCount;
            lock (countsFound) {
                maxIncrementDone = Math.Max(maxIncrementDone, (int)(rs >> rangeIncrementBits));
                incompleteRange = incrementsDone++ < maxIncrementDone;
                (firstMissingCount, nextMissingCount, missingCount) = UpdateFound(countsFound, rangeEvenCounts);
            }

            double density = 1.0 - missingCount / ((double)countsFound.Count - firstMissingCount);
            Console.WriteLine(
                $"{(incompleteRange ? "* " : "")}{message} First missing count = {firstMissingCount:N0}, next = {nextMissingCount:N0}, highest count = {countsFound.Count - 1:N0}, missing count = {missingCount:N0}, density = {density * 100:N2}%.");
        });
    }

    // returns all primes in the uint space
    public static uint[] GetPrimes() {
        Console.Write("Finding primes...");
        uint[] primes = new uint[203_280_221];
        int index = 0;
        primes[index++] = 2;
        // uint.MaxValue is 4,294,967,295 which is composite
        // int.MaxValue is 2,147,483,647 (x2 + 1) = 4,294,967,295
        BitArray isOddComposite = new(int.MaxValue);
        isOddComposite[int.MaxValue - 1] = true; // mark 4,294,967,293 (2^32-3) as composite
        uint sqrtLimit = 65536;
        for (uint n = 3; n < sqrtLimit; n += 2)
            if (!isOddComposite[(int)(n >> 1)]) {
                primes[index++] = n;
                for (uint add = n << 1, comp = n + add; comp > add && comp != uint.MaxValue; comp += add)
                    isOddComposite[(int)(comp >> 1)] = true;
            }

        Console.Write(' ');
        for (uint n = sqrtLimit + 1; n < uint.MaxValue; n += 2)
            if (!isOddComposite[(int)(n >> 1)])
                primes[index++] = n;

        Console.WriteLine($"done. Found {index:N0} primes. Highest prime is {primes[index - 1]:N0}.");
        return primes;
    }

    public static string FileNameForRange(uint rangeStart, uint rangeEnd) => $"counts-{rangeStart}-{rangeEnd}.gz";

    public static string ReadRange(uint rangeStart, uint rangeEnd, uint[] rangeEvenCounts) {
        string fileName = FileNameForRange(rangeStart, rangeEnd);
        if (!File.Exists(fileName))
            return null;

        using FileStream file = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using StreamReader sr = new(gzip);
        int index = 0;
        while (sr.ReadLine() is { } line)
            rangeEvenCounts[index++] = uint.Parse(line);

        return $"Counts for range [{rangeStart:N0}...{rangeEnd:N0}] read from disk.";
    }

    public static string ComputeRange(uint[] primes, uint rangeStart, uint rangeEnd, uint[] rangeEvenCounts) {
        if (rangeStart == 0)
            rangeEvenCounts[2]++; // 4 can be expressed as 2+2, but we skip 2 since this is the only case using it.

        int pmax = Array.BinarySearch(primes, rangeEnd >> 1);
        if (pmax < 0) pmax = ~pmax;
        for (int pi = 1; pi <= pmax; pi++) { // skip the first prime (2)
            uint p = primes[pi];
            int qi = rangeStart > p
                ? ~Array.BinarySearch(primes, rangeStart - p - 1)
                : pi; // rangeStart is even, p is odd, so we subtract 1 to guarantee that we won't find it exactly
            if (qi < pi) qi = pi;
            // rangeEnd is odd, p is odd, so we will not find it exactly
            int qmax = ~Array.BinarySearch(primes, rangeEnd - p);
            for (uint rsmp = rangeStart - p; qi < qmax; qi++)
                rangeEvenCounts[(primes[qi] - rsmp) >> 1]++;
        }

        return null;
    }

    public static string WriteRange(uint rangeStart, uint rangeEnd, uint[] rangeEvenCounts) {
        using FileStream fs = new FileStream(FileNameForRange(rangeStart, rangeEnd), FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        using GZipStream gz = new(fs, CompressionMode.Compress);
        using StreamWriter sw = new(gz);
        foreach (uint evenCount in rangeEvenCounts)
            sw.WriteLine(evenCount);

        return $"Counts for range [{rangeStart:N0}...{rangeEnd:N0}] computed and saved.";
    }

    public static (int, int, int) UpdateFound(List<bool> found, uint[] rangeEvenCounts) {
        foreach (uint count in rangeEvenCounts) {
            while (count > found.Count)
                found.Add(false);
            
            if (count < found.Count)
                found[(int)count] = true;
            else
                found.Add(true);
        }

        int firstMissingCount = 0;
        while (firstMissingCount < found.Count && found[firstMissingCount])
            firstMissingCount++;

        int nextMissingCount = firstMissingCount + 1;
        while (nextMissingCount < found.Count && found[nextMissingCount])
            nextMissingCount++;

        int missingCount = 1;
        for (int i = nextMissingCount; i < found.Count; i++)
            if (!found[i])
                missingCount++;

        return (firstMissingCount, nextMissingCount, missingCount);
    }
}