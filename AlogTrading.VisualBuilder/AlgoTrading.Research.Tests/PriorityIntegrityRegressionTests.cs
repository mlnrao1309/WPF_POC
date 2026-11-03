using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Research.Analysis;
using Xunit;

public class PriorityIntegrityRegressionTests
{
    static CandidatePatch Patch(string op,string path,JsonNode? value=null)=>new(){Description="integrity regression",Operations=new(){new(){Op=op,Path=path,Value=value}}};
    [Fact] public void Revert_PersistedHistoryRestoresExactBytes() {
        const string source="{ \"first\" : 1,\"nullable\":null,\"last\":3 }";
        var candidate=CandidateGenerator.Generate(source,Patch("remove","/first"));
        var persisted=JsonSerializer.Deserialize<List<AppliedPatchRecord>>(JsonSerializer.Serialize(candidate.AppliedPatches))!;
        Assert.Equal(source,CandidateGenerator.Revert(candidate.CandidateJson,persisted));
    }
    [Fact] public void Revert_RejectsTamperedCandidate() {
        var candidate=CandidateGenerator.Generate("{\"a\":1}",Patch("replace","/a",JsonValue.Create(2)));
        Assert.Throws<InvalidOperationException>(()=>CandidateGenerator.Revert("{\"a\":3}",candidate.AppliedPatches));
    }
    [Fact] public void Revert_RejectsTamperedOriginalSnapshot() {
        var candidate=CandidateGenerator.Generate("{\"a\":1}",Patch("replace","/a",JsonValue.Create(2)));
        var history=candidate.AppliedPatches.Select(p=>p with {OriginalDocumentJson="{\"a\":9}"}).ToArray();
        Assert.Throws<InvalidOperationException>(()=>CandidateGenerator.Revert(candidate.CandidateJson,history));
    }
    [Fact] public void Revert_NoOperationsPreservesFormatting() {
        const string source="{ \"a\" : 1 }";
        var candidate=CandidateGenerator.Generate(source,new(){Description="no-op",Operations=new()});
        Assert.Equal(source,candidate.CandidateJson); Assert.Equal(source,CandidateGenerator.Revert(candidate.CandidateJson,candidate.AppliedPatches));
    }
    [Fact] public void Pointer_EmptyPropertySegmentIsNotDiscarded() {
        var candidate=CandidateGenerator.Generate("{\"\":{\"a\":1}}",Patch("replace","//a",JsonValue.Create(2)));
        Assert.Equal(2,JsonNode.Parse(candidate.CandidateJson)![""]!["a"]!.GetValue<int>());
    }
    [Fact] public void ArrayTest_MissingIsNotNull() {
        Assert.Throws<InvalidOperationException>(()=>CandidateGenerator.Generate("{\"a\":[null]}",Patch("test","/a/9")));
    }
}
