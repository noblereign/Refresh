using Refresh.GameServer.Types.Contests;

namespace Refresh.GameServer.Endpoints.ApiV3.DataTypes.Response;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class ApiContestResponse : IApiResponse
{
    public required string ContestId { get; set; }
    public required ApiGameUserResponse Organizer { get; set; }
    public required DateTimeOffset CreationDate { get; set; }
    public required DateTimeOffset StartDate { get; set; }
    public required DateTimeOffset EndDate { get; set; }
    public required string ContestTag { get; set; }
    public required string BannerUrl { get; set; }
    public required string ContestTitle { get; set; }
    public required string ContestSummary { get; set; }
    public required string ContestDetails { get; set; }
    
    public static ApiContestResponse? FromOld(GameContest? old)
    {
        if (old == null) return null;
        return new ApiContestResponse
        {
            ContestId = old.ContestId,
            Organizer = ApiGameUserResponse.FromOld(old.Organizer)!,
            CreationDate = old.CreationDate,
            StartDate = old.StartDate,
            EndDate = old.EndDate,
            ContestTag = old.ContestTag,
            BannerUrl = old.BannerUrl,
            ContestTitle = old.ContestTitle,
            ContestSummary = old.ContestSummary,
            ContestDetails = old.ContestDetails,
        };
    }
    
    public static IEnumerable<ApiContestResponse> FromOldList(IEnumerable<GameContest> oldList) => oldList.Select(FromOld).ToList()!;
}