namespace GoldbachConjecture;

[TestClass]
public sealed class GoldbachCometGapTests {
    [TestMethod]
    public void CheckComputeRange0To34() {
        uint[] primes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31];
        uint[] rangeEvenCounts = new uint[36 >> 1];
        // 0={}
        // 2={}
        // 4=2+2
        // 6=3+3
        // 8=5+3
        // 10=5+5, 3+7
        // 12=5+7
        // 14=7+7, 3+11
        // 16=3+13, 5+11
        // 18=5+13, 7+11
        // 20=3+17, 7+13
        // 22=3+19, 5+17, 11+11
        // 24=5+19, 7+17, 11+13
        // 26=3+23, 7+19, 13+13
        // 28=5+23, 11+17
        // 30=7+23, 11+19, 13+17
        // 32=3+29, 13+19
        // 34=3+31, 5+29, 11+23, 17+17
        string message = GoldbachCometGap.ComputeRange(primes, 0, 35, rangeEvenCounts);
        Assert.IsNull(message);
        CollectionAssert.AreEqual(new uint[] { 0, 0, 1, 1, 1, 2, 1, 2, 2, 2, 2, 3, 3, 3, 2, 3, 2, 4 }, rangeEvenCounts);
    }
    
    [TestMethod]
    public void CheckComputeRange36To40() {
        uint[] primes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37];
        uint[] rangeEvenCounts = new uint[6 >> 1];
        // 36=5+31, 7+29, 13+23, 17+19
        // 38=7+31, 19+19
        // 40=3+37, 11+29, 17+23
        string message = GoldbachCometGap.ComputeRange(primes, 36, 41, rangeEvenCounts);
        Assert.IsNull(message);
        CollectionAssert.AreEqual(new uint[] { 4, 2, 3 }, rangeEvenCounts);
    }
}