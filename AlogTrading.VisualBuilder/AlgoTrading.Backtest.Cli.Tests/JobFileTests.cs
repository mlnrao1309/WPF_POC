using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class JobFileTests
{
    [Theory]
    [InlineData("RealizedOnly")]
    [InlineData("IncludeUnrealized")]
    public void Load_PreservesRiskChoice(string basis)
    {
        var path=WriteJob("{\"schemaVersion\":1,\"strategyFile\":\"x.json\",\"instrumentToken\":1,\"from\":\"2026-01-01\",\"to\":\"2026-02-01\",\"mode\":\"Single\",\"lossBasis\":\""+basis+"\",\"tradedCapital\":50000}");
        try
        {
            var job=JobFile.Load(path);
            Assert.Equal(Enum.Parse<AlgoTrading.Models.RiskLossBasis>(basis),job.LossBasis);
            Assert.Equal(50000m,job.TradedCapital);
        }
        finally{File.Delete(path);}
    }
    private static string WriteJob(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "job-" + Guid.NewGuid().ToString("N") + ".job.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_Valid()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1,
  ""strategyFile"": ""EmaCross.json"",
  ""instrumentToken"": 2885,
  ""from"": ""2020-01-01"",
  ""to"": ""2024-12-31"",
  ""mode"": ""Single"",
  ""strategyId"": 12
}");
        try
        {
            var j = JobFile.Load(path);
            Assert.Equal(1, j.SchemaVersion);
            Assert.Equal("EmaCross.json", j.StrategyFile);
            Assert.Equal(2885u, j.InstrumentToken);
            Assert.Equal(new DateOnly(2020, 1, 1), j.From);
            Assert.Equal(new DateOnly(2024, 12, 31), j.To);
            Assert.Equal(JobMode.Single, j.Mode);
            Assert.Equal(12, j.StrategyId);
            Assert.Null(j.TunedId);
            Assert.Equal(AlgoTrading.Models.RiskLossBasis.RealizedOnly,j.LossBasis);
            Assert.Equal(50000m,j.TradedCapital);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Sweep_Mode()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": ""x.json"",
  ""instrumentToken"": 1, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Sweep""
}");
        try
        {
            var j = JobFile.Load(path);
            Assert.Equal(JobMode.Sweep, j.Mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_WalkForward_Mode()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": ""x.json"",
  ""instrumentToken"": 1, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""WalkForward""
}");
        try
        {
            var j = JobFile.Load(path);
            Assert.Equal(JobMode.WalkForward, j.Mode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Wrong_SchemaVersion_Rejected()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 2, ""strategyFile"": ""x.json"",
  ""instrumentToken"": 1, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Single""
}");
        try { Assert.Throws<InvalidDataException>(() => JobFile.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Missing_StrategyFile_Rejected()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": """",
  ""instrumentToken"": 1, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Single""
}");
        try { Assert.Throws<InvalidDataException>(() => JobFile.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Path_In_StrategyFile_Rejected()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": ""../etc/passwd.json"",
  ""instrumentToken"": 1, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Single""
}");
        try { Assert.Throws<InvalidDataException>(() => JobFile.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Zero_Token_Rejected()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": ""x.json"",
  ""instrumentToken"": 0, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Single""
}");
        try { Assert.Throws<InvalidDataException>(() => JobFile.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_From_Greater_Than_To_Rejected()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": ""x.json"",
  ""instrumentToken"": 1, ""from"": ""2024-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Single""
}");
        try { Assert.Throws<InvalidDataException>(() => JobFile.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Bad_Mode_Rejected()
    {
        var path = WriteJob(@"{
  ""schemaVersion"": 1, ""strategyFile"": ""x.json"",
  ""instrumentToken"": 1, ""from"": ""2020-01-01"", ""to"": ""2020-12-31"",
  ""mode"": ""Bogus""
}");
        try { Assert.Throws<InvalidDataException>(() => JobFile.Load(path)); }
        finally { File.Delete(path); }
    }
}
