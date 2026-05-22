using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class PermissionService(IDbContextFactory<HttpForge.Data.AppDbContext> dbFactory) { }
