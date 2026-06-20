using System.Collections.Concurrent;

namespace Attendance.Api.Services;

/// <summary>
/// In-memory OTP store (employee code -> otp). Singleton, resets on restart.
/// Demo mode: OTP is returned in the API response (no real SMS).
/// </summary>
public class OtpService
{
    private readonly ConcurrentDictionary<string, string> _otps = new();
    private readonly Random _rng = new();

    /// <summary>Generate a 6-digit OTP for the key (employee code) and store it.</summary>
    public string Generate(string key)
    {
        var otp = _rng.Next(100000, 1000000).ToString();
        _otps[key] = otp;
        return otp;
    }

    /// <summary>True if the supplied OTP matches the stored one for the key.</summary>
    public bool Verify(string key, string otp)
        => _otps.TryGetValue(key, out var saved) && saved == otp;
}
