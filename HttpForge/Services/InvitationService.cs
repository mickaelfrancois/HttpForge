using Microsoft.EntityFrameworkCore;

namespace HttpForge.Services;

public class InvitationService(IDbContextFactory<HttpForge.Data.AppDbContext> dbFactory) { }
