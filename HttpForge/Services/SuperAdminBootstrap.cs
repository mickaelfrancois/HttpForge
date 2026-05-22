using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace HttpForge.Services;

public static class SuperAdminBootstrap
{
    public static async Task EnsureAsync(IServiceProvider services)
    {
        var email = Environment.GetEnvironmentVariable("HTTPFORGE_SUPERADMIN_EMAIL");
        if (string.IsNullOrEmpty(email)) return;

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("SuperAdmin"))
            await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));

        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var existing = await userManager.GetUsersInRoleAsync("SuperAdmin");
        if (existing.Count > 0) return;

        var dbFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync();

        var normalizedEmail = email.ToLower().Trim();
        var alreadyInvited = await db.InvitationTokens
            .AnyAsync(i => i.Email == normalizedEmail && i.UsedAt == null);
        if (alreadyInvited) return;

        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToHexString(bytes).ToLower();
        db.InvitationTokens.Add(new InvitationToken
        {
            Email = normalizedEmail,
            Role = "SuperAdmin",
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(365)
        });
        await db.SaveChangesAsync();

        Console.WriteLine("=== HttpForge SuperAdmin invitation ===");
        Console.WriteLine($"Email : {normalizedEmail}");
        Console.WriteLine($"Link  : /invite/{token}");
        Console.WriteLine("=======================================");

        var smtpSettings = services.GetRequiredService<IOptions<SmtpSettings>>().Value;
        if (smtpSettings.IsConfigured)
        {
            var appUrl = smtpSettings.AppUrl?.TrimEnd('/') ?? "http://localhost:5078";
            var emailSender = services.GetRequiredService<EmailSender>();
            try
            {
                await emailSender.SendInvitationAsync(normalizedEmail, $"{appUrl}/invite/{token}", "SuperAdmin");
                Console.WriteLine("Invitation email sent.");
            }
            catch
            {
                Console.WriteLine("Warning: failed to send invitation email (check SMTP config).");
            }
        }
    }
}
