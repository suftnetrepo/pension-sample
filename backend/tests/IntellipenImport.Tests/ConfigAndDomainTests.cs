using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

public class ConfigAndDomainTests
{
    // ---- Config: every shipped file is valid; broken ones are refused with reasons ----

    [Fact]
    public void Every_shipped_schema_and_the_mapping_are_valid()
    {
        Assert.NotEmpty(AllSchemas);
        Assert.All(AllSchemas, s => Assert.Empty(ConfigRules.CheckSchema(s)));
        Assert.Empty(ConfigRules.CheckMapping(Mapping));
    }

    [Fact]
    public void A_schema_missing_a_required_field_or_using_an_unknown_code_is_refused()
    {
        var schema = new SourceSchema
        {
            SourceName = "Test", Scheme = "X",
            Fields = { ["MemberId"] = new FieldMapping { Column = "ID" }, ["Status"] = new FieldMapping { Column = "S", Codes = new() { ["R"] = "Retired" } } },
            Salaries = { new SalaryColumn { Period = "Y-1", Column = "Pay" } },
        };
        var problems = string.Join("; ", ConfigRules.CheckSchema(schema));
        Assert.Contains("required canonical field 'NiNumber' is not mapped", problems);
        Assert.Contains("code value 'Retired' is not one of Active, Deferred", problems);
    }

    [Fact]
    public void A_mapping_reading_an_unknown_field_or_missing_a_code_is_refused()
    {
        var mapping = new TargetMapping
        {
            Name = "x", Version = "1",
            Records = { new TargetRecordDefinition { Type = "MEM", Fields = {
                new TargetFieldDefinition { Name = "A", From = "Postcode" },
                new TargetFieldDefinition { Name = "B", From = "Status", Codes = new() { ["Active"] = "ACT" } } } } },
        };
        var problems = string.Join("; ", ConfigRules.CheckMapping(mapping));
        Assert.Contains("'Postcode' is not a canonical field", problems);
        Assert.Contains("no IntelliPen code for 'Deferred'", problems);
    }

    // ---- Domain: batch status rules ----

    private static BatchRecord Open() => new() { Id = "IMP-1", Status = BatchStatus.ReadyForReview, ExpiresAtUtc = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero) };

    [Fact]
    public void A_batch_can_be_committed_once_only()
    {
        var b = Open();
        b.MarkCommitted("a.user", DateTimeOffset.UnixEpoch, new CommitOutcome());
        Assert.Equal(BatchStatus.Committed, b.Status);
        Assert.Throws<BatchRuleException>(() => b.MarkCommitted("a.user", DateTimeOffset.UnixEpoch, new CommitOutcome()));
        Assert.Throws<BatchRuleException>(() => b.MarkDiscarded("a.user", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Expiry_is_decided_by_the_clock()
    {
        var b = Open();
        Assert.False(b.HasExpired(new DateTimeOffset(2026, 11, 2, 23, 59, 0, TimeSpan.Zero)));
        Assert.True(b.HasExpired(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void An_unbalanced_or_empty_batch_cannot_be_committed()
    {
        Assert.Throws<BatchRuleException>(() => new HeldBatch { Result = new ImportResult { Balanced = false, Accepted = 5 } }.EnsureCommittable());
        Assert.Throws<BatchRuleException>(() => new HeldBatch { Result = new ImportResult { Balanced = true, Accepted = 0 } }.EnsureCommittable());
    }
}
