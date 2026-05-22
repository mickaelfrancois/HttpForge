using HttpForge.Data;
using HttpForge.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        db.InvitationTokens.Add(new InvitationToken
        {
            Email = normalizedEmail,
            Role = "SuperAdmin",
            Token = Convert.ToHexString(bytes).ToLower(),
            ExpiresAt = DateTime.UtcNow.AddDays(365)
        });
        await db.SaveChangesAsync();
    }
}
