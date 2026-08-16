using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Data;

/// <summary>
/// Parser for the hstore-like "other_tags" strings in the supplied OSM
/// GeoJSON export, e.g.:
///
/// "lanes"=>"2","lit"=>"yes","surface"=>"asphalt"
/// </summary>
public static class OsmOtherTagsParser
{
    public static IReadOnlyDictionary<string, string> Parse(
        string? source)
    {
        Dictionary<string, string> result =
            new(
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(
                source))
        {
            return result;
        }

        int index =
            0;

        while (index <
            source.Length)
        {
            SkipSeparators(
                source,
                ref index);

            if (!TryReadQuoted(
                    source,
                    ref index,
                    out string key))
            {
                break;
            }

            SkipWhitespace(
                source,
                ref index);

            if (index + 1 >=
                    source.Length ||
                source[index] != '=' ||
                source[index + 1] != '>')
            {
                break;
            }

            index +=
                2;

            SkipWhitespace(
                source,
                ref index);

            if (!TryReadQuoted(
                    source,
                    ref index,
                    out string value))
            {
                break;
            }

            result[key] =
                value;
        }

        return result;
    }

    private static void SkipSeparators(
        string source,
        ref int index)
    {
        while (index <
               source.Length &&
               (char.IsWhiteSpace(
                    source[index]) ||
                source[index] == ','))
        {
            index++;
        }
    }

    private static void SkipWhitespace(
        string source,
        ref int index)
    {
        while (index <
               source.Length &&
               char.IsWhiteSpace(
                    source[index]))
        {
            index++;
        }
    }

    private static bool TryReadQuoted(
        string source,
        ref int index,
        out string value)
    {
        value =
            string.Empty;

        if (index >=
                source.Length ||
            source[index] != '"')
        {
            return false;
        }

        index++;

        int start =
            index;

        while (index <
            source.Length)
        {
            if (source[index] == '"')
            {
                value =
                    source.Substring(
                        start,
                        index -
                        start);

                index++;

                return true;
            }

            index++;
        }

        return false;
    }
}
