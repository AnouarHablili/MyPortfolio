using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MyPortfolio.Shared.Configuration;

namespace MyPortfolio.Web.Services;

/// <summary>
/// Service for generating and validating JWT tokens for access code authentication.
/// </summary>
public class JwtTokenService
{
    private readonly ILogger<JwtTokenService> _logger;
    private readonly AccessCodeOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(ILogger<JwtTokenService> logger, IOptions<AccessCodeOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        // Generate a signing key from the access code (in production, use a dedicated secret key)
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(_options.Code + "JWT_SIGNING_KEY"));
        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    /// <summary>
    /// Generates a JWT token for the given access code.
    /// </summary>
    public string GenerateToken(string accessCode)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, accessCode),
            new Claim("access_code", accessCode),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var expiresAt = DateTime.UtcNow.AddHours(_options.TokenExpirationHours);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(
                _signingKey,
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        _logger.LogInformation("JWT token generated successfully. Expires at: {ExpiresAt}", expiresAt);

        return tokenString;
    }

    /// <summary>
    /// Validates a JWT token and returns the claims principal if valid.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero // No clock skew tolerance
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("JWT token has expired");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "JWT token signature is invalid");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token");
            return null;
        }
    }

    /// <summary>
    /// Extracts the expiration time from a JWT token without full validation.
    /// </summary>
    public DateTime? GetTokenExpiration(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);
            return jwtToken.ValidTo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading JWT token expiration");
            return null;
        }
    }
}

