using Application.DTOs.Tags;
using Domain.Tags;

namespace Application.Mapping;

public static class TagDtoMapper
{
    public static RealtimeTagDto ToDto(TagRuntimeState state) =>
        new(state.TagId, state.DisplayName, FormatDisplayValue(state), state.Unit ?? "", state.Quality, state.AlarmState, state.Timestamp);

    private static string FormatDisplayValue(TagRuntimeState state)
    {
        if (state.NumericValue.HasValue)
        {
            return state.NumericValue.Value.ToString("0.###");
        }

        if (state.BoolValue.HasValue)
        {
            return state.BoolValue.Value ? "True" : "False";
        }

        return state.TextValue ?? "";
    }
}
