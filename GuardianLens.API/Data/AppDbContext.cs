using GuardianLens.API.Models;
using GuardianLens.API.Services;
using Microsoft.EntityFrameworkCore;

namespace GuardianLens.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DigitalAsset> Assets { get; set; }
    public DbSet<Violation> Violations { get; set; }
    public DbSet<ScanJob> ScanJobs { get; set; }
    public DbSet<AlertEvent> AlertEvents { get; set; }   // NEW — alert history log

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DigitalAsset>(b =>
        {
            b.HasIndex(a => a.PHash);
            b.HasMany(a => a.Violations).WithOne(v => v.DigitalAsset)
             .HasForeignKey(v => v.DigitalAssetId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(a => a.ScanJobs).WithOne(s => s.DigitalAsset)
             .HasForeignKey(s => s.DigitalAssetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertEvent>(b =>
            b.HasIndex(a => a.OccurredAt));
    }
}

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Assets.Any()) return;

        var assets = new[]
        {
            new DigitalAsset { Title = "IPL 2024 Final — Last Over Highlights", Sport = "Cricket",
                Organization = "BCCI", Type = AssetType.Highlight,
                PHash = "A3F5C2B19E4D7A0F", WatermarkToken = "BCCI-2024-HL-001",
                FilePath = "wwwroot/assets/demo1.png", RegisteredAt = DateTime.UtcNow.AddDays(-30) },
            new DigitalAsset { Title = "Rohit Sharma Century — Official Broadcast", Sport = "Cricket",
                Organization = "BCCI", Type = AssetType.Broadcast,
                PHash = "5E1A8C3F7B2D6094", WatermarkToken = "BCCI-2024-BR-044",
                FilePath = "wwwroot/assets/demo2.png", RegisteredAt = DateTime.UtcNow.AddDays(-14) },
            new DigitalAsset { Title = "Pro Kabaddi League — Season Opener", Sport = "Kabaddi",
                Organization = "Mashal Sports", Type = AssetType.VideoClip,
                PHash = "B7D49E0A1C3F852D", WatermarkToken = "PKL-2024-OPN-001",
                FilePath = "wwwroot/assets/demo3.png", RegisteredAt = DateTime.UtcNow.AddDays(-7) },
            new DigitalAsset { Title = "ISL Match 12 — Official Match Photos", Sport = "Football",
                Organization = "FSDL", Type = AssetType.Image,
                PHash = "2F6A0B9C4D7E3158", WatermarkToken = "ISL-2024-M12-PHO",
                FilePath = "wwwroot/assets/demo4.png", RegisteredAt = DateTime.UtcNow.AddDays(-3) },
        };
        db.Assets.AddRange(assets);
        db.SaveChanges();

        var violations = new[]
        {
            new Violation { DigitalAssetId=1, Platform="YouTube",
                InfringingUrl="https://youtube.com/watch?v=pirated001",
                MatchConfidence=0.97, Status=ViolationStatus.TakedownSent,
                DetectedAt=DateTime.UtcNow.AddDays(-25), TakedownReference="GL-DMCA-20240310-00001" },
            new Violation { DigitalAssetId=1, Platform="Telegram",
                InfringingUrl="https://t.me/sportleaks/1234",
                MatchConfidence=0.94, Status=ViolationStatus.Detected,
                DetectedAt=DateTime.UtcNow.AddHours(-3) },
            new Violation { DigitalAssetId=2, Platform="YouTube",
                InfringingUrl="https://youtube.com/watch?v=stolen_highlights",
                MatchConfidence=0.99, Status=ViolationStatus.Detected,
                DetectedAt=DateTime.UtcNow.AddHours(-1) },
            new Violation { DigitalAssetId=2, Platform="Instagram",
                InfringingUrl="https://instagram.com/p/pirated_post",
                MatchConfidence=0.88, Status=ViolationStatus.UnderReview,
                DetectedAt=DateTime.UtcNow.AddHours(-6) },
            new Violation { DigitalAssetId=3, Platform="Reddit",
                InfringingUrl="https://reddit.com/r/kabaddi/stolen_clip",
                MatchConfidence=0.93, Status=ViolationStatus.Detected,
                DetectedAt=DateTime.UtcNow.AddMinutes(-45) },
            new Violation { DigitalAssetId=4, Platform="Twitter",
                InfringingUrl="https://twitter.com/sportspage/stolen_photo",
                MatchConfidence=0.96, Status=ViolationStatus.TakedownSent,
                DetectedAt=DateTime.UtcNow.AddDays(-2), TakedownReference="GL-DMCA-20240402-00004" },
        };
        db.Violations.AddRange(violations);

        var scans = new[]
        {
            new ScanJob { DigitalAssetId=1, Status=ScanStatus.Completed,
                CreatedAt=DateTime.UtcNow.AddDays(-1), CompletedAt=DateTime.UtcNow.AddDays(-1).AddMinutes(3),
                UrlsScanned=1247, ViolationsFound=2 },
            new ScanJob { DigitalAssetId=2, Status=ScanStatus.Completed,
                CreatedAt=DateTime.UtcNow.AddHours(-4), CompletedAt=DateTime.UtcNow.AddHours(-4).AddMinutes(2),
                UrlsScanned=943, ViolationsFound=2 },
        };
        db.ScanJobs.AddRange(scans);

        var alerts = new[]
        {
            new AlertEvent { ViolationId=1, EventType="AutoTakedown",
                Message="Auto-DMCA filed for YouTube piracy (97% match)", OccurredAt=DateTime.UtcNow.AddDays(-25) },
            new AlertEvent { ViolationId=3, EventType="HighAlert",
                Message="99% match requires review — Rohit Sharma highlight stolen", OccurredAt=DateTime.UtcNow.AddHours(-1) },
        };
        db.AlertEvents.AddRange(alerts);

        db.SaveChanges();
    }
}
