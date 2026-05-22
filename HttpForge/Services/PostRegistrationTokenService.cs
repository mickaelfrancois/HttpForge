using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HttpForge.Services;

public class PostRegistrationTokenService
{
    private readonly ConcurrentDictionary<string, (string UserId, DateTime Expires)> _tokens = new();

    public string CreateToken(string userId)
    {
        // Purge expired tokens opportunistically
        var now = DateTime.UtcNow;
        foreach (var key in _tokens.Keys)
            if (_tokens.TryGetValue(key, out var v) && v.Expires < now)
                _tokens.TryRemove(key, out _);

        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToHexString(bytes).ToLower();
        _tokens[token] = (userId, now.AddMinutes(5));
        return token;
    }

    // Validates and consumes the token atomically — returns userId or null.
    public string? Consume(string token)
    {
        if (!_tokens.TryRemove(token, out var entry)) return null;
        if (entry.Expires < DateTime.UtcNow) return null;
        return entry.UserId;
    }
}
