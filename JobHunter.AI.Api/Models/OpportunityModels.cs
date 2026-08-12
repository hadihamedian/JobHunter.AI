namespace JobHunter.AI.Api.Models
{
    public static class OpportunityType
    {
        public const string FullTimeRemote = "FULL_TIME_REMOTE";
        public const string PartTime = "PART_TIME";
        public const string Freelance = "FREELANCE";
        public const string FixedPrice = "FIXED_PRICE";
        public const string HourlyContract = "HOURLY_CONTRACT";
        public const string Subcontract = "SUBCONTRACT";
        public const string AgencyPartnership = "AGENCY_PARTNERSHIP";
        public const string AiProject = "AI_PROJECT";
        public const string PotentialClient = "POTENTIAL_CLIENT";
        public const string Other = "OTHER";
    }

    public static class RemoteCompatibility
    {
        public const string Green = "GREEN";     // Worldwide / offshore / contractors accepted
        public const string Yellow = "YELLOW";   // Unclear
        public const string Red = "RED";         // Geographically restricted
        public const string Unknown = "UNKNOWN"; // Not enough info to judge
    }

    public static class OpportunityStatus
    {
        public const string Discovered = "DISCOVERED";
        public const string Analyzing = "ANALYZING";
        public const string Recommended = "RECOMMENDED";
        public const string Reviewed = "REVIEWED";
        public const string Applied = "APPLIED";
        public const string Interview = "INTERVIEW";
        public const string Won = "WON";
        public const string Lost = "LOST";
        public const string Rejected = "REJECTED";
        public const string Skipped = "SKIPPED";
    }

    public static class OpportunityRecommendation
    {
        public const string Apply = "APPLY";
        public const string Review = "REVIEW";
        public const string Skip = "SKIP";
    }

    public record RawOpportunity(
        string Source,              // "apify:linkedin-jobs", "rss:remoteok", ...
        string? ExternalId,
        string? SourceUrl,
        string Title,
        string? Description,
        string? Company,
        string? Client,
        string? Location,
        string? RemoteTypeRaw,     
        string EngagementTypeRaw,   
        string? BudgetRaw,
        string? CurrencyRaw,
        List<string>? SkillsRaw,
        DateTimeOffset? PostedAt,
        string RawJson             
    );

    public record Opportunity(
        Guid Id,
        string Source,
        string? ExternalId,
        string? SourceUrl,
        string Title,
        string? Description,
        string? Company,
        string? Client,
        string? Location,
        string? RemoteType,
        string EngagementType,      
        decimal? BudgetMin,
        decimal? BudgetMax,
        string? Currency,
        List<string> Skills,
        DateTimeOffset? PostedAt,
        DateTimeOffset CollectedAt,
        string Status,                
        string DedupKey
    );

    public record UpdateOpportunityStatusRequest(string Status);

    public record OpportunityFilter(
        string? Status = null,
        string? Source = null,
        string? EngagementType = null,
        string? RemoteCompatibility = null,
        int? MinScore = null
    );

   
    public record OpportunityAnalysis(
        Guid Id,
        Guid OpportunityId,
        int TechnicalMatch,
        int ExperienceMatch,
        int BudgetScore,
        int ClientQualityScore,        
        int CompetitionScore,          
        int RemoteCompatibilityScore,
        int AiRelevanceScore,
        int OverallScore,
        string Recommendation,        
        double Confidence,
        List<string> Reasons,
        List<string> Risks,
        List<string> MissingSkills,
        string? SuggestedStrategy,
        string RemoteCompatibilityFlag,  
        string? RemoteCompatibilityReason,
        DateTimeOffset AnalyzedAt
    );
}
