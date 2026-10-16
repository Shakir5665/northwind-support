using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core mapping for ticket escalation audit records (BR-7).
/// </summary>
public sealed class TicketEscalationConfiguration : IEntityTypeConfiguration<TicketEscalation>
{
    public void Configure(EntityTypeBuilder<TicketEscalation> builder)
    {
        builder.ToTable("TicketEscalations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromPriority).HasConversion<int>();

        builder.Property(e => e.ToPriority).HasConversion<int>();

        builder.Property(e => e.Reason).HasMaxLength(500).IsRequired();

        builder.Property(e => e.EscalatedBy).HasMaxLength(100).IsRequired();

        builder.Property(e => e.EscalatedAtUtc).HasColumnType("datetime2(3)");

        builder.Property(e => e.FromDueAtUtc).HasColumnType("datetime2(3)");

        builder.Property(e => e.ToDueAtUtc).HasColumnType("datetime2(3)");

        // Relationship to Ticket aggregate: cascading delete ensures ticket removal clears history.
        builder.HasOne<Ticket>()
            .WithMany(t => t.Escalations)
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Historical agent references never cascade delete audit records.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.FromAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.ToAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ticket-specific history retrieval
        builder.HasIndex(e => e.TicketId)
            .HasDatabaseName("IX_TicketEscalations_TicketId");

        // Covers newest-first sorting for ticket escalation history queries without a sort pass
        builder.HasIndex(e => new { e.TicketId, e.EscalatedAtUtc })
            .HasDatabaseName("IX_TicketEscalations_TicketId_EscalatedAtUtc");
    }
}
