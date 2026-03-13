using System.Security.Cryptography;

namespace TaoMaster.Core.Services;

public sealed class ChecksumService
{
    public async Task VerifyAsync(
        string filePath,
        string expectedChecksum,
        string algorithm,
        CancellationToken cancellationToken)
    {
        var actualChecksum = await ComputeAsync(filePath, algorithm, cancellationToken);

        if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"校验失败: {Path.GetFileName(filePath)} 的 {algorithm} 不匹配。期望 {expectedChecksum}，实际 {actualChecksum}。");
        }
    }

    private static async Task<string> ComputeAsync(string filePath, string algorithm, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = algorithm.ToUpperInvariant() switch
        {
            "SHA256" => await SHA256.HashDataAsync(stream, cancellationToken),
            "SHA512" => await SHA512.HashDataAsync(stream, cancellationToken),
            _ => throw new ArgumentException($"不支持的校验算法: {algorithm}", nameof(algorithm))
        };

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
