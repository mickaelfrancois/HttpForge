using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class TeamService(IDbContextFactory<HttpForge.Data.AppDbContext> dbFactory) { }
