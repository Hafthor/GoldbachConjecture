using System.Diagnostics;
using System.IO.Compression;

namespace GoldbachConjecture;

public static class GoldbachCometGap {
    public static void Execute() {
        int max = Array.MaxLength,
            iterLimit = (int)Math.Sqrt(max);
        //iterLimit = max / 3; // 0X7FFFFFC7 = 2,147,483,591 - divide by smallest odd prime (715,827,863 r 2)
        List<int> primes = []; // we don't bother with 2.
        { // scope for isOddPrime
            bool[] isOddPrime = new bool[max >> 1]; // we don't bother with evens (including 2)
            Array.Fill(isOddPrime, true, 1, isOddPrime.Length - 1); // 1 is not prime, others are potentially prime

            Console.Write("Finding primes... ");
            int prime = 3;
            for (; prime <= iterLimit; prime += 2)
                if (isOddPrime[prime >> 1]) {
                    primes.Add(prime);
                    // i=715,827,863 is the largest odd at/below iterLimit, so i2+i=2,147,483,589 which is below Array.MaxLength (2,147,483,591)
                    // clear all odd multiples of prime starting at prime*3 (since prime*1 and prime*2 are prime and even respectively)
                    for (int prime2 = prime << 1, multiple = prime + prime2; (uint)multiple < max; multiple += prime2)
                        isOddPrime[multiple >> 1] = false;
                }

            for (; prime < max; prime += 2)
                if (isOddPrime[prime >> 1])
                    primes.Add(prime);
        }
        int primeLimit = primes.BinarySearch((max >> 1) + 1);
        if (primeLimit < 0) primeLimit = ~primeLimit;
        Console.WriteLine($"done. Found {primes.Count:N0} primes. Half-way point at {primeLimit:N0} primes.");
        // verify here: https://t5k.org/nthprime/index.php#nth
        // 105,097,563rd prime should be 2,147,483,587
        Console.WriteLine(
            $"Sanity check: {(primes.Count + 1):N0}th prime = {primes[^1]:N0}"); // add 1 for "2" which we didn't add to primes list

        uint[] evenCounts = new uint[(max >> 1) + 1];
        evenCounts[2]++; // for 4 (which can only be 2+2, so one combination)

        int halfMax = max >> 1, percent = 0, pl100000 = primeLimit / 100000, nextDone = pl100000;
        if (primes[primeLimit - 1] >= halfMax) throw new InvalidOperationException("Prime limit is too high");
        if (primes[primeLimit] < halfMax) throw new InvalidOperationException("Prime limit is too low");
        Stopwatch sw = Stopwatch.StartNew();
        object progressLock = new();
        ManualResetEventSlim pauseGate = new(initialState: true);
        ParallelOptions pOpt = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };
        (int startIndex, TimeSpan elapsed) = ReadProgress(evenCounts);
        if (startIndex != 0) {
            percent = startIndex / pl100000;
            nextDone = (percent + 1) * pl100000;
            Console.WriteLine($"Resuming from n={primes[startIndex]:N0} ({percent * 0.001:N3}%). elapsed={elapsed}");
        }

        int index = startIndex - 1, runningThreads = 0;
        Parallel.For(startIndex, primeLimit, pOpt, _ => {
            pauseGate.Wait();
            Interlocked.Increment(ref runningThreads);
            int i = Interlocked.Increment(ref index), n = primes[i];
            for (int i2 = i; i2 < primes.Count; i2++) {
                int nn = n + primes[i2];
                if ((uint)nn >= (uint)max) break;
                Interlocked.Increment(ref evenCounts[nn >> 1]);
            }

            bool waitForResume = false;
            if (i == nextDone) {
                lock (progressLock) {
                    nextDone += pl100000;
                    percent++;
                    double doneRatio = percent / 100000.0, leftRatio = 1 - doneRatio;
                    long elapsedSec = (long)(elapsed + sw.Elapsed).TotalSeconds;
                    TimeSpan estimatedLeft = TimeSpan.FromSeconds((long)(elapsedSec * leftRatio / doneRatio));
                    Console.Write(
                        $"\r{percent * 0.001:N3}% - n={n:N0}, elapsed={TimeSpan.FromSeconds(elapsedSec)}, eta={estimatedLeft}\e[K");

                    if (!Console.IsInputRedirected && Console.KeyAvailable) {
                        Console.ReadKey(intercept: true); // consume pause key
                        pauseGate.Reset();
                        sw.Stop();
                        Console.Write(" (PAUSING)");
                        waitForResume = true;
                    }
                }
            }

            if (waitForResume) {
                // Note that the progress would be incorrect if we saved before all threads have paused,
                // so we wait for them to pause.
                while (Volatile.Read(ref runningThreads) > 1)
                    Thread.Sleep(10); // wait for other threads to notice pause and stop
                Console.Write(new string('\b', " (PAUSING)".Length) + " (SAVING)\e[K");
                WriteProgress(index, elapsed + sw.Elapsed, evenCounts);
                Console.Write(new string('\b', " (SAVING)".Length) + " (PAUSED)\e[K");
                Console.ReadKey(intercept: true);
                lock (progressLock) {
                    Console.Write(new string('\b', " (PAUSED)".Length) + "\e[K");
                    pauseGate.Set();
                    sw.Start();
                }
            }

            Interlocked.Decrement(ref runningThreads);
        });

        sw.Stop();
        Console.Write($"\rFinished! Elapsed={elapsed + sw.Elapsed}. Writing out results... \e[K");
        WriteProgress(index, elapsed + sw.Elapsed, evenCounts);
        Console.WriteLine("done.");

        Console.Write("Scanning for highest and first missing count... ");
        int highestCount = 0;
        for (int i = 0; i < evenCounts.Length; i++)
            if (evenCounts[i] > highestCount)
                highestCount = (int)evenCounts[i];

        bool[] found = new bool[highestCount + 1];
        for (int i = 0; i < evenCounts.Length; i++)
            found[evenCounts[i]] = true;

        int firstMissingCount = 0, densityCount = 0;
        for (; firstMissingCount < max; firstMissingCount++)
            if (found[firstMissingCount])
                break;
        for (int i = firstMissingCount + 1; i <= highestCount; i++)
            if (found[i])
                densityCount++;

        Console.WriteLine(
            $"done. Highest count found = {highestCount:N0}. First missing count = {firstMissingCount:N0}. Density count = {densityCount:N0}.");
    }

    private const string progressFile = "primesums-progress.gz";

    // progress file format:
    //     highest prime index finished, elapsed time so far
    //     count for 0
    //     count for 2
    //     count for 4
    //     ...
    private static (int, TimeSpan) ReadProgress(uint[] evenCounts) {
        if (!File.Exists(progressFile)) return (0, TimeSpan.Zero);
        using FileStream file = File.Open(progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using StreamReader sr = new(gzip);
        // first line (lastPrimeIndexDone, elapsed)
        string line = sr.ReadLine();
        if (line == null) return (0, TimeSpan.Zero);
        string[] parts = line.Split(',', 2);
        int lastPrimeIndexDone = int.Parse(parts[0]);
        TimeSpan elapsed = parts.Length < 2 ? TimeSpan.Zero : TimeSpan.Parse(parts[1]);
        // read even counts
        for (int i = 0; i < evenCounts.Length; i++) {
            line = sr.ReadLine();
            if (line == null) {
                Array.Clear(evenCounts, i, evenCounts.Length - i); // just to be sure
                break;
            }

            evenCounts[i] = uint.Parse(line);
        }

        int nextPrimeIndex = lastPrimeIndexDone + 1;
        return (nextPrimeIndex, elapsed);
    }

    private static void WriteProgress(int lastPrimeIndexDone, TimeSpan elapsed, uint[] evenCounts) {
        int highest = 0;
        for (int i = 0; i < evenCounts.Length; i++)
            if (evenCounts[i] > 0)
                highest = i;
        using FileStream file = new(progressFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using GZipStream gz = new(file, CompressionMode.Compress);
        using StreamWriter sw = new(gz);
        sw.WriteLine(lastPrimeIndexDone + "," + elapsed);
        for (int i = 0; i <= highest; i++)
            sw.WriteLine(evenCounts[i]);
    }
}