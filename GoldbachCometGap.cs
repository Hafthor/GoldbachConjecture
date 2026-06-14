using System.Diagnostics;
using System.IO.Compression;

namespace GoldbachConjecture;

public static class GoldbachCometGap {
    public static void Execute() {
        // can be up to (uint)Array.MaxLength * 2, but then requires sieve and main add loop
        // can be up to uint.MaxValue - 65536*2 and not require overflow checking in sieve and main add loop
        //uint max = uint.MaxValue - 65536 * 2;
        uint max = 1_000_000_000;
        Console.WriteLine($"max={max:N0}");
        uint maxHeadroom = uint.MaxValue - max; // ca-ca-catch the wave
        int iterLimit = (int)Math.Sqrt(max);
        // if this check fails, then we need to have an overflow check in the sieve depriming loop
        if ((uint)iterLimit * 2 > maxHeadroom)
            throw new InvalidOperationException($"2x iterLimit ({iterLimit * 2}) is greater than max headroom ({maxHeadroom})");
        uint maxPrimeGap = 0;
        List<uint> primes = [2]; // we will usually skip 2
        { // scope for isOddPrime
            bool[] isOddPrime = new bool[max >> 1]; // we don't bother with evens (including 2)
            Array.Fill(isOddPrime, true, 1, isOddPrime.Length - 1); // 1 is not prime, others are potentially prime

            Console.Write("Finding primes... ");
            uint prime = 3, prevPrime = 2;
            for (; prime <= iterLimit; prime += 2)
                if (isOddPrime[prime >> 1]) {
                    primes.Add(prime);
                    // i=715,827,863 is the largest odd at/below iterLimit, so i2+i=2,147,483,589 which is below Array.MaxLength (2,147,483,591)
                    // clear all odd multiples of prime starting at prime*3 (since prime*1 and prime*2 are prime and even respectively)
                    for (uint prime2 = prime << 1, multiple = prime + prime2; multiple < max; multiple += prime2)
                        isOddPrime[multiple >> 1] = false;
                    maxPrimeGap = Math.Max(maxPrimeGap, prime - prevPrime);
                    prevPrime = prime;
                }

            for (; prime < max; prime += 2)
                if (isOddPrime[prime >> 1]) {
                    primes.Add(prime);
                    maxPrimeGap = Math.Max(maxPrimeGap, prime - prevPrime);
                    prevPrime = prime;
                }
        }
        
        // if this check fails, you need to either lower the max OR
        // use the alternate nn >= max check and disable this headRoom check
        if (maxPrimeGap > maxHeadroom) 
            throw new InvalidOperationException($"Maximum prime gap {maxPrimeGap} is too high for 2^32-max {maxHeadroom}");

        int primeLimit = primes.BinarySearch((max >> 1) + 1);
        if (primeLimit < 0) primeLimit = ~primeLimit;
        Console.WriteLine($"done. Found {primes.Count:N0} primes. Half-way point at {primeLimit:N0} primes.");
        
        // verify here: https://t5k.org/nthprime/index.php#nth
        // 105,097,563rd prime should be 2,147,483,587 (if max is Array.MaxLength)
        Console.WriteLine($"Sanity check: {primes.Count:N0}th prime = {primes[^1]:N0}");

        uint[] evenCounts = new uint[(max >> 1) + 1];
        evenCounts[2]++; // for 4 (which can only be 2+2, so one combination)

        int halfMax = (int)(max >> 1), percent = 0, pl100000 = primeLimit / 100000, nextDone = pl100000;
        if (primes[primeLimit - 1] >= halfMax) throw new InvalidOperationException("Prime limit is too high");
        if (primes[primeLimit] < halfMax) throw new InvalidOperationException("Prime limit is too low");
        Stopwatch sw = Stopwatch.StartNew();
        object progressLock = new();
        ManualResetEventSlim pauseGate = new(initialState: true);
        ParallelOptions pOpt = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };
        (max, int startIndex, TimeSpan elapsed) = ReadProgress(evenCounts, max);
        if (startIndex != 0) {
            percent = startIndex / pl100000;
            nextDone = (percent + 1) * pl100000;
            Console.WriteLine($"Resuming from n={primes[startIndex]:N0} ({percent * 0.001:N3}%). elapsed={elapsed}");
        } else {
            startIndex = 1; // we skip the prime "2"
        }

        int index = startIndex - 1, runningThreads = 0;
        Parallel.For(startIndex, primeLimit, pOpt, _ => {
            pauseGate.Wait();
            Interlocked.Increment(ref runningThreads);
            int i = Interlocked.Increment(ref index);
            uint n = primes[i];
            for (int i2 = i; i2 < primes.Count; i2++) {
                uint nn = n + primes[i2];
                if (nn >= max) break; // could this overflow? (yes, if distance between primes > 2^32-max)
                //if (nn >= max || nn < n) break; // alternate check (if max is close enough to uint.MaxValue that overflow is a concern)
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
                WriteProgress(max, index, elapsed + sw.Elapsed, evenCounts);
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
        WriteProgress(max, index, elapsed + sw.Elapsed, evenCounts);
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
            if (!found[firstMissingCount])
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
    private static (uint, int, TimeSpan) ReadProgress(uint[] evenCounts, uint max) {
        if (!File.Exists(progressFile)) return (max, 0, TimeSpan.Zero);
        using FileStream file = File.Open(progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using StreamReader sr = new(gzip);
        // first line (max, lastPrimeIndexDone, elapsed)
        string line = sr.ReadLine();
        if (line == null) return (max, 0, TimeSpan.Zero);
        string[] parts = line.Split(',', 3);
        uint newMax = uint.Parse(parts[0]);
        // currently, we cannot use progress file for a different max value
        if (max != newMax) return (max, 0, TimeSpan.Zero);
        int lastPrimeIndexDone = int.Parse(parts[1]); // ok to explode if missing
        TimeSpan elapsed = parts.Length < 3 ? TimeSpan.Zero : TimeSpan.Parse(parts[2]);
        
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
        return (newMax, nextPrimeIndex, elapsed);
    }

    private static void WriteProgress(uint max, int lastPrimeIndexDone, TimeSpan elapsed, uint[] evenCounts) {
        int highest = 0;
        for (int i = 0; i < evenCounts.Length; i++)
            if (evenCounts[i] > 0)
                highest = i;
        using FileStream file = new(progressFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using GZipStream gz = new(file, CompressionMode.Compress);
        using StreamWriter sw = new(gz);
        sw.WriteLine(max + "," + lastPrimeIndexDone + "," + elapsed);
        for (int i = 0; i <= highest; i++)
            sw.WriteLine(evenCounts[i]);
    }
}