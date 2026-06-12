using Domain.Tags;

namespace Domain.Rules;

public static class TagValidationRule
{
    public static TagQuality ValidateRange(double value, TagDefinition definition)
    {
        if (definition.MinValue.HasValue && value < definition.MinValue.Value)
        {
            return TagQuality.OutOfRange;
        }

        if (definition.MaxValue.HasValue && value > definition.MaxValue.Value)
        {
            return TagQuality.OutOfRange;
        }

        return TagQuality.Good;
    }
}
