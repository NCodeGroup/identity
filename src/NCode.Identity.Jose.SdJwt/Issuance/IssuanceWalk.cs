// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using NCode.Identity.Jose.SdJwt.Digests;

namespace NCode.Identity.Jose.SdJwt.Issuance;

/// <summary>
/// One depth-first walk over the claims of a Selective Disclosure JWT being issued: it moves each disclosable member
/// or element into a disclosure and writes its digest in its place, in the <c>_sd</c> array of the object or as a
/// <c>{"...": digest}</c> placeholder in the array, and adds the decoys (RFC 9901 §4.2.4, §4.2.5).
/// </summary>
/// <param name="algorithm">The hash algorithm of the digests.</param>
/// <param name="randomSource">The source of salts and decoy positions.</param>
/// <param name="disclosablePaths">The pointers of the disclosable claims; each is removed as it is found.</param>
/// <param name="decoyDigestCount">The number of decoys added to each <c>_sd</c> array and to each array with a
/// disclosable element.</param>
internal sealed class IssuanceWalk(
    SdJwtDigestAlgorithm algorithm,
    ISdJwtRandomSource randomSource,
    HashSet<string> disclosablePaths,
    int decoyDigestCount
)
{
    private SdJwtDigestAlgorithm Algorithm { get; } = algorithm;
    private ISdJwtRandomSource RandomSource { get; } = randomSource;
    private HashSet<string> DisclosablePaths { get; } = disclosablePaths;
    private int DecoyDigestCount { get; } = decoyDigestCount;

    /// <summary>
    /// Gets the disclosures created so far, in the order they were created.
    /// </summary>
    public List<Disclosure> Disclosures { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the walk wrote any digest, a decoy included, so the payload needs an
    /// <c>_sd_alg</c> claim.
    /// </summary>
    public bool WroteDigests { get; private set; }

    /// <summary>
    /// Walks an object, its members first.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <param name="path">The pointer of the object.</param>
    public void ProcessObject(JsonObject obj, string path)
    {
        List<string>? digests = null;
        var names = new List<string>(obj.Count);
        foreach (var member in obj)
            names.Add(member.Key);

        foreach (var name in names)
        {
            var memberPath = JsonPointer.AppendMember(path, name);
            ProcessNode(obj[name], memberPath);

            if (!DisclosablePaths.Remove(memberPath))
                continue;

            var value = Detach(obj, name);
            var salt = RandomSource.NextSalt();
            var disclosure = Disclosure.Create(salt, name, value);
            Disclosures.Add(disclosure);
            var disclosureDigest = disclosure.ComputeDigest(Algorithm);
            (digests ??= []).Add(disclosureDigest);
        }

        if (digests is null)
            return;

        for (var i = 0; i < DecoyDigestCount; i++)
        {
            var decoyDigest = CreateDecoyDigest();
            digests.Add(decoyDigest);
        }

        // The order of the digests says nothing about the claims once it is the order of the digests themselves.
        digests.Sort(StringComparer.Ordinal);

        var sd = new JsonArray();
        foreach (var digest in digests)
        {
            var digestValue = JsonValue.Create(digest);
            sd.Add(digestValue);
        }

        obj[JoseClaimNames.Payload.Sd] = sd;
        WroteDigests = true;
    }

    private void ProcessArray(JsonArray array, string path)
    {
        var disclosed = false;
        for (var index = 0; index < array.Count; index++)
        {
            var elementPath = JsonPointer.AppendIndex(path, index);
            ProcessNode(array[index], elementPath);

            if (!DisclosablePaths.Remove(elementPath))
                continue;

            var value = Detach(array, index);
            var salt = RandomSource.NextSalt();
            var disclosure = Disclosure.Create(salt, value);
            Disclosures.Add(disclosure);
            var disclosureDigest = disclosure.ComputeDigest(Algorithm);
            array[index] = CreatePlaceholder(disclosureDigest);
            disclosed = true;
        }

        if (!disclosed)
            return;

        for (var i = 0; i < DecoyDigestCount; i++)
        {
            var insertIndex = RandomSource.NextIndex(array.Count + 1);
            var decoyDigest = CreateDecoyDigest();
            var placeholder = CreatePlaceholder(decoyDigest);
            array.Insert(insertIndex, placeholder);
        }

        WroteDigests = true;
    }

    private void ProcessNode(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                ProcessObject(obj, path);
                break;

            case JsonArray array:
                ProcessArray(array, path);
                break;
        }
    }

    private string CreateDecoyDigest()
    {
        var salt = RandomSource.NextSalt();
        return Algorithm.ComputeDigest(salt);
    }

    private static JsonObject CreatePlaceholder(string digest) =>
        new() { [JoseClaimNames.Payload.SdArrayElement] = digest };

    private static JsonElement Detach(JsonObject obj, string name)
    {
        var node = obj[name];
        obj.Remove(name);
        return ToElement(node);
    }

    private static JsonElement Detach(JsonArray array, int index)
    {
        var node = array[index];
        array[index] = null;
        return ToElement(node);
    }

    private static JsonElement ToElement(JsonNode? node) =>
        node is null
            ? JsonSerializer.SerializeToElement<object?>(null)
            : JsonSerializer.SerializeToElement(node);
}
