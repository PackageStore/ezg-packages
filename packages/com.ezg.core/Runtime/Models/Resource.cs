using Newtonsoft.Json;

[System.Serializable]
public class Resource
{
    /// <summary>
    /// A resource type value (int const). The catalog of valid values lives in the consuming project.
    /// </summary>
    [JsonProperty("type")] public int resType;

    /// <summary>
    /// Unique identify of a type.
    /// </summary>
    [JsonProperty("id")] public int resId;

    /// <summary>
    /// Number of resource.
    /// </summary>
    [JsonProperty("number")] public long resNumber;


    public float[] customValue;

    public virtual Resource Clone()
    {
        return (Resource)this.MemberwiseClone();
    }

    public Resource()
    {
    }

    public Resource(int resType, int resId, long resNumber)
    {
        this.resType = resType;
        this.resId = resId;
        this.resNumber = resNumber;
    }
}