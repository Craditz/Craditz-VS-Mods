using System.Collections.Generic;

namespace FeralKinship;

public sealed class FeralKinshipConfig
{
    public bool AllowAdultTaming { get; set; } = true;

    public bool AllowBabyTaming { get; set; } = false;

    public List<string> EnabledTamingAnimals { get; set; } = new()
    {
        "bear",
        "chicken",
        "deer",
        "fox",
        "gazelle",
        "goat",
        "hare",
        "hyena",
        "cat",
        "pig",
        "raccoon",
        "sheep",
        "wolf"
    };
}
