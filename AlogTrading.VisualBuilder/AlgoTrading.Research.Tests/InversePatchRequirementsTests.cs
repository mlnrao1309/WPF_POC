using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoTrading.Research.Analysis;
using Xunit;

public class InversePatchRequirementsTests
{
    static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    static JsonPatchOperation Op(string op,string path,JsonNode? value=null)=>new(){Op=op,Path=path,Value=value};
    static CandidatePatch Patch(string? hash,params JsonPatchOperation[] ops)=>new(){Description="inverse contract",ExpectedParentHash=hash,Operations=ops.ToList()};
    const string Original="{\"schemaVersion\":1,\"strategyName\":\"Audit\",\"audit\":{\"first\":1,\"nullable\":null,\"last\":3},\"auditArray\":[1,null,3]}";

    [Theory]
    [InlineData("replace","/audit/first")]
    [InlineData("replace","/audit/nullable")]
    [InlineData("add","/audit/new")]
    [InlineData("add","/audit/first")]
    [InlineData("add","/audit/nullable")]
    [InlineData("remove","/audit/first")]
    [InlineData("remove","/audit/nullable")]
    [InlineData("replace","/auditArray/1")]
    [InlineData("add","/auditArray/1")]
    [InlineData("add","/auditArray/-")]
    [InlineData("remove","/auditArray/1")]
    public void Inverse_RestoresJsonStructure(string op,string path) {
        var result=CandidateGenerator.Generate(Original,Patch(Hash(Original),Op(op,path,op=="remove" ? null : JsonValue.Create(99))));
        var reverted=CandidateGenerator.Revert(result.CandidateJson,result.AppliedPatches);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Original),JsonNode.Parse(reverted)));
    }
    [Fact] public void Inverse_ReverseOrderHandlesDependentArrayAndObjectChanges() {
        var result=CandidateGenerator.Generate(Original,Patch(Hash(Original),
            Op("remove","/auditArray/0"),Op("add","/auditArray/1",JsonValue.Create(7)),
            Op("replace","/auditArray/0",JsonValue.Create(8)),Op("add","/auditArray/-",JsonValue.Create(9)),
            Op("remove","/audit"),Op("add","/audit",new JsonObject { ["replacement"]=true })));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Original),JsonNode.Parse(CandidateGenerator.Revert(result.CandidateJson,result.AppliedPatches))));
    }
    [Fact] public void Inverse_DistinguishesMissingFromExistingNull() {
        var missing=CandidateGenerator.Generate(Original,Patch(null,Op("add","/audit/new",JsonValue.Create(1))));
        var present=CandidateGenerator.Generate(Original,Patch(null,Op("add","/audit/nullable",JsonValue.Create(1))));
        Assert.False(Assert.Single(missing.AppliedPatches).HadOldValue);
        Assert.True(Assert.Single(present.AppliedPatches).HadOldValue);
    }
    [Fact] public void ExpectedParentHash_RejectsStaleSource() {
        Assert.Throws<InvalidOperationException>(()=>CandidateGenerator.Generate(Original,Patch(new string('0',64),Op("replace","/audit/first",JsonValue.Create(2)))));
    }
    [Fact] public void ExpectedParentHash_AcceptsMatchingUppercaseHash() {
        var result=CandidateGenerator.Generate(Original,Patch(Hash(Original).ToUpperInvariant(),Op("replace","/audit/first",JsonValue.Create(2))));
        Assert.Equal(Hash(Original),result.OriginalHash);
    }
    [Fact] public void Inverse_EscapedPointerRoundTrip() {
        var source="{\"audit/key~x\":1}";
        var result=CandidateGenerator.Generate(source,Patch(Hash(source),Op("replace","/audit~1key~0x",JsonValue.Create(2))));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source),JsonNode.Parse(CandidateGenerator.Revert(result.CandidateJson,result.AppliedPatches))));
    }
    [Fact] public void Inverse_CompactOriginalHashMustBeReproducedAsDocumented() {
        var result=CandidateGenerator.Generate(Original,Patch(Hash(Original),Op("replace","/audit/first",JsonValue.Create(2))));
        Assert.Equal(result.OriginalHash,Hash(CandidateGenerator.Revert(result.CandidateJson,result.AppliedPatches)));
    }
    [Fact] public void Inverse_IndentedOriginalHashAfterRemoveMustBeReproducedAsDocumented() {
        var source=JsonNode.Parse(Original)!.ToJsonString(new JsonSerializerOptions {WriteIndented=true});
        var result=CandidateGenerator.Generate(source,Patch(Hash(source),Op("remove","/audit/first")));
        Assert.Equal(result.OriginalHash,Hash(CandidateGenerator.Revert(result.CandidateJson,result.AppliedPatches)));
    }
    [Fact] public void PatchTest_MissingIsNotEquivalentToJsonNull() {
        Assert.Throws<InvalidOperationException>(()=>CandidateGenerator.Generate(Original,Patch(null,Op("test","/audit/missing",null))));
    }
}
