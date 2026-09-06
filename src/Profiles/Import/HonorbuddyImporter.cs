using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WoWBuddy.Profiles.Import;

/// <summary>
/// Converts an Honorbuddy questing profile into a WoWBuddy one, and says what it could not do.
/// </summary>
/// <remarks>
/// <para>
/// People have years of questing profiles for the bot this one replaces, and rewriting them by
/// hand is not realistic. So the importer exists — but its honest job is reporting, not
/// conversion. Honorbuddy's format was never specified publicly, its conditions are arbitrary
/// C#, and much of what its profiles do lives in custom behaviours that are code. A converter
/// that quietly produced a profile out of all that would be lying about how much of it it
/// understood.
/// </para>
/// <para>
/// So: convert what the vocabulary in <see cref="HonorbuddyVocabulary"/> covers, and for
/// everything else say which element, on which line, and what was lost. A step whose condition
/// could not be translated is kept, because dropping it loses quest progress, but it is
/// counted and the import is not called clean while any remain.
/// </para>
/// <para>
/// The conversion goes through WoWBuddy XML rather than straight to objects, so the user gets a
/// file they can read, fix and keep, and so the ordinary loader validates the result. Nothing
/// from Honorbuddy is used or included here beyond the names of XML elements in the user's own
/// files.
/// </para>
/// </remarks>
public static class HonorbuddyImporter
{
    /// <summary>True when this looks like an Honorbuddy profile rather than a WoWBuddy one.</summary>
    public static bool LooksLikeHonorbuddy(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        try
        {
            XElement? root = XDocument.Parse(xml).Root;

            return root is not null
                && HonorbuddyVocabulary.RootElements.Contains(root.Name.LocalName, StringComparer.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>Converts a profile file.</summary>
    /// <param name="path">The Honorbuddy .xml to read.</param>
    /// <param name="mapId">
    /// Which map the profile is for. Honorbuddy profiles rarely say, so this has to be told to
    /// the importer; passing the wrong one produces a profile that navigates nowhere.
    /// </param>
    public static ProfileImportResult ImportFile(string path, int mapId = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string source = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            return Failed(source, $"There is no profile at '{path}'.");
        }

        try
        {
            return Import(File.ReadAllText(path), source, mapId);
        }
        catch (IOException exception)
        {
            return Failed(source, $"'{path}' could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed(source, $"'{path}' could not be read: {exception.Message}");
        }
    }

    /// <summary>Converts a profile already in memory.</summary>
    /// <param name="xml">The Honorbuddy profile's text.</param>
    /// <param name="source">Where it came from, for messages.</param>
    /// <param name="mapId">Which map the profile is for.</param>
    public static ProfileImportResult Import(string xml, string source = "profile", int mapId = 0)
    {
        List<ProfileIssue> issues = [];

        XDocument document;

        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Error,
                $"This is not valid XML: {exception.Message}",
                exception.LineNumber));

            return new ProfileImportResult(null, string.Empty, issues, source);
        }

        XElement? root = document.Root;

        if (root is null)
        {
            return Failed(source, "The file is empty.");
        }

        if (!HonorbuddyVocabulary.RootElements.Contains(root.Name.LocalName, StringComparer.OrdinalIgnoreCase))
        {
            return Failed(
                source,
                $"The outermost element is <{root.Name.LocalName}>. An Honorbuddy profile starts "
                + $"with <{HonorbuddyVocabulary.RootElements[0]}>; a WoWBuddy one loads directly.");
        }

        Conversion conversion = new(issues, mapId);
        XElement converted = conversion.Convert(root, source);

        string convertedXml = Render(converted, source);

        ProfileLoadResult loaded = ProfileLoader.Parse(convertedXml, source);
        issues.AddRange(loaded.Issues);

        return new ProfileImportResult(
            loaded.Profile,
            convertedXml,
            issues,
            source,
            conversion.StepsConverted,
            conversion.StepsNotUnderstood,
            conversion.ConditionsConverted,
            conversion.ConditionsLost);
    }

    private static ProfileImportResult Failed(string source, string message) =>
        new(
            null,
            string.Empty,
            [new ProfileIssue(ProfileIssueSeverity.Error, message)],
            source);

    private static string Render(XElement profile, string source)
    {
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XComment(
                $" Converted from the Honorbuddy profile {source} by WoWBuddy.{Environment.NewLine}"
                + $"     Read the import report before running it: anything the importer could not{Environment.NewLine}"
                + $"     translate is listed there, not here. "),
            profile);

        Utf8StringWriter text = new();

        XmlWriterSettings settings = new()
        {
            Indent = true,
            Encoding = Encoding.UTF8,
        };

        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            document.Save(writer);
        }

        return text.ToString();
    }

    /// <summary>
    /// One conversion, holding the counts as it goes.
    /// </summary>
    private sealed class Conversion(List<ProfileIssue> issues, int mapId)
    {
        public int StepsConverted { get; private set; }

        public int StepsNotUnderstood { get; private set; }

        public int ConditionsConverted { get; private set; }

        public int ConditionsLost { get; private set; }

        public XElement Convert(XElement root, string source)
        {
            XElement profile = new("Profile");

            string name = TextOf(root, "Name") ?? source;
            profile.SetAttributeValue("Name", name);

            if (TextOf(root, "Author") is { } author)
            {
                profile.SetAttributeValue("Author", author);
            }

            if (TextOf(root, "MinLevel") is { } minimum)
            {
                profile.SetAttributeValue("MinLevel", minimum);
            }

            if (TextOf(root, "MaxLevel") is { } maximum)
            {
                profile.SetAttributeValue("MaxLevel", maximum);
            }

            // Honorbuddy profiles almost never state a map: the bot knew it from where the
            // character was standing. Ours needs one, so it comes from the caller, and saying
            // so out loud is better than a profile that silently navigates on the wrong map.
            profile.SetAttributeValue("Map", mapId.ToString(CultureInfo.InvariantCulture));
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Warning,
                $"Every step was put on map {mapId}, because Honorbuddy profiles do not record "
                + "one. If this profile is not for that map, change Map on the <Profile> element.",
                LineOf(root),
                root.Name.LocalName));

            XElement questOrder = new("QuestOrder");
            bool foundQuestOrder = false;

            foreach (XElement section in root.Elements())
            {
                string local = section.Name.LocalName;

                if (HonorbuddyVocabulary.QuestOrderElements.Contains(local, StringComparer.OrdinalIgnoreCase))
                {
                    foundQuestOrder = true;
                    ConvertSteps(section, questOrder);
                    continue;
                }

                switch (local.ToUpperInvariant())
                {
                    case "NAME" or "AUTHOR" or "MINLEVEL" or "MAXLEVEL" or "MINDURABILITY" or "MINFREEBAGSLOTS":
                        break;

                    case "VENDORS":
                        profile.Add(ConvertVendors(section));
                        break;

                    case "MAILBOXES" or "MAILBOXLIST":
                        profile.Add(ConvertMailboxes(section));
                        break;

                    case "BLACKSPOTS":
                        profile.Add(ConvertBlackspots(section));
                        break;

                    case "AVOIDMOBS":
                        profile.Add(ConvertAvoidMobs(section));
                        break;

                    default:
                        Note(section, $"<{local}> is not a section the importer knows, so it was left out.");
                        break;
                }
            }

            if (!foundQuestOrder)
            {
                issues.Add(new ProfileIssue(
                    ProfileIssueSeverity.Error,
                    $"There is no <{HonorbuddyVocabulary.QuestOrderElements[0]}> in this profile, "
                    + "so there is nothing to convert.",
                    LineOf(root)));
            }

            profile.Add(questOrder);
            return profile;
        }

        private void ConvertSteps(XElement parent, XElement into)
        {
            foreach (XElement element in parent.Elements())
            {
                string local = element.Name.LocalName;

                switch (local.ToUpperInvariant())
                {
                    case "IF":
                        ConvertBlock(element, into, "If");
                        break;

                    case "WHILE":
                        ConvertBlock(element, into, "While");
                        break;

                    case "PICKUP":
                        Emit(into, QuestStep(element, "PickUp"));
                        break;

                    case "TURNIN":
                        Emit(into, QuestStep(element, "TurnIn"));
                        break;

                    case "KILLMOBS":
                        Emit(into, Objective(element, "Kill"));
                        break;

                    case "COLLECTITEM":
                        Emit(into, Objective(element, "Collect"));
                        break;

                    case "USEITEM":
                        Emit(into, Objective(element, "UseItem"));
                        break;

                    case "INTERACTWITH":
                        Emit(into, Objective(element, "Interact"));
                        break;

                    case "RUNTO" or "MOVETO":
                        Emit(into, RunTo(element));
                        break;

                    case "CUSTOMBEHAVIOR":
                        Emit(into, CustomBehavior(element));
                        break;

                    default:
                        Skip(
                            element,
                            $"<{local}> is not a step the importer knows, so whatever it did is "
                            + "missing from the converted profile.");
                        break;
                }
            }
        }

        private void ConvertBlock(XElement element, XElement into, string name)
        {
            XElement block = new(name);

            string? condition = ConditionOf(element);

            if (condition is null)
            {
                // A While with no usable condition would repeat forever, and an If with none
                // would run its contents always. Neither is a safe thing to emit, so the
                // contents are lifted out of an If and the whole While is dropped.
                if (name == "While")
                {
                    Skip(
                        element,
                        "<While> had a condition the importer could not translate, so the whole "
                        + "loop was left out rather than emitted as one that never ends.");
                    return;
                }

                ConvertSteps(element, into);
                return;
            }

            block.SetAttributeValue("Condition", condition);
            ConvertSteps(element, block);

            if (!block.HasElements)
            {
                return;
            }

            into.Add(block);
        }

        private XElement QuestStep(XElement element, string name)
        {
            XElement step = new(name);

            CopyId(element, step, "QuestId", HonorbuddyVocabulary.QuestIdAttributes);
            CopyText(element, step, "QuestName", HonorbuddyVocabulary.QuestNameAttributes);
            CopyId(element, step, "Entry", HonorbuddyVocabulary.EntryAttributes);
            CopyPosition(element, step);
            CopyCondition(element, step);
            ReportUnknownAttributes(element);

            return step;
        }

        private XElement Objective(XElement element, string type)
        {
            XElement step = new("Objective");
            step.SetAttributeValue("Type", type);

            CopyId(element, step, "QuestId", HonorbuddyVocabulary.QuestIdAttributes);
            CopyText(element, step, "QuestName", HonorbuddyVocabulary.QuestNameAttributes);
            CopyId(element, step, "Entry", HonorbuddyVocabulary.EntryAttributes);
            CopyId(element, step, "ItemId", HonorbuddyVocabulary.ItemIdAttributes);
            CopyNumber(element, step, "Count", HonorbuddyVocabulary.CountAttributes);
            CopyNumber(element, step, "Radius", HonorbuddyVocabulary.RadiusAttributes);
            CopyNumber(element, step, "Index", HonorbuddyVocabulary.ObjectiveIndexAttributes);
            CopyPosition(element, step);
            CopyCondition(element, step);
            CopyHotspots(element, step);
            ReportUnknownAttributes(element);

            return step;
        }

        private XElement RunTo(XElement element)
        {
            XElement step = new("RunTo");

            CopyPosition(element, step);
            CopyCondition(element, step);
            ReportUnknownAttributes(element);

            return step;
        }

        private XElement CustomBehavior(XElement element)
        {
            XElement step = new("CustomBehavior");

            string behaviour = AttributeOf(element, ["File", "Name", "Type"]) ?? string.Empty;
            step.SetAttributeValue("Name", behaviour);

            // Honorbuddy custom behaviours are compiled C# shipped alongside the profile, so
            // nothing here can run one. The name and its arguments are carried across so the
            // step is visible and can be written as a WoWBuddy behaviour later.
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Warning,
                behaviour.Length > 0
                    ? $"<CustomBehavior> runs '{behaviour}', which is Honorbuddy code and cannot be "
                        + "converted. The step was kept so it can be written as a WoWBuddy behaviour, "
                        + "but it will not do anything until one exists by that name."
                    : "<CustomBehavior> names no behaviour, so nothing can be done with it.",
                LineOf(element),
                "CustomBehavior"));

            foreach (XAttribute attribute in element.Attributes())
            {
                string local = attribute.Name.LocalName;

                if (attribute.IsNamespaceDeclaration
                    || local.Equals("File", StringComparison.OrdinalIgnoreCase)
                    || local.Equals("Name", StringComparison.OrdinalIgnoreCase)
                    || local.Equals("Type", StringComparison.OrdinalIgnoreCase)
                    || HonorbuddyVocabulary.ConditionAttributes.Contains(local, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                step.SetAttributeValue(local, attribute.Value);
            }

            CopyCondition(element, step);
            return step;
        }

        private XElement ConvertVendors(XElement section)
        {
            XElement vendors = new("Vendors");

            foreach (XElement element in section.Elements())
            {
                if (!element.Name.LocalName.Equals("Vendor", StringComparison.OrdinalIgnoreCase))
                {
                    Note(element, $"<{element.Name.LocalName}> is not a vendor the importer knows.");
                    continue;
                }

                XElement vendor = new("Vendor");
                CopyText(element, vendor, "Name", ["Name"]);
                CopyId(element, vendor, "Entry", HonorbuddyVocabulary.EntryAttributes);
                CopyPosition(element, vendor);

                string? type = AttributeOf(element, ["Type"]);

                if (type is not null && type.Contains("Repair", StringComparison.OrdinalIgnoreCase))
                {
                    vendor.SetAttributeValue("Repair", "true");
                }

                vendors.Add(vendor);
            }

            return vendors;
        }

        private XElement ConvertMailboxes(XElement section)
        {
            XElement vendors = new("Vendors");

            foreach (XElement element in section.Elements())
            {
                XElement mailbox = new("Mailbox");
                CopyText(element, mailbox, "Name", ["Name"]);
                CopyId(element, mailbox, "Entry", HonorbuddyVocabulary.EntryAttributes);
                CopyPosition(element, mailbox);
                vendors.Add(mailbox);
            }

            return vendors;
        }

        private XElement ConvertBlackspots(XElement section)
        {
            XElement blackspots = new("Blackspots");

            foreach (XElement element in section.Elements())
            {
                if (!element.Name.LocalName.Equals("Blackspot", StringComparison.OrdinalIgnoreCase))
                {
                    Note(element, $"<{element.Name.LocalName}> is not a blackspot the importer knows.");
                    continue;
                }

                XElement blackspot = new("Blackspot");
                CopyPosition(element, blackspot);
                CopyNumber(element, blackspot, "Radius", HonorbuddyVocabulary.RadiusAttributes);
                CopyText(element, blackspot, "Reason", ["Reason", "Name"]);
                blackspots.Add(blackspot);
            }

            return blackspots;
        }

        private XElement ConvertAvoidMobs(XElement section)
        {
            XElement mobs = new("AvoidMobs");

            foreach (XElement element in section.Elements())
            {
                XElement mob = new("Mob");
                CopyId(element, mob, "Entry", HonorbuddyVocabulary.EntryAttributes);
                CopyText(element, mob, "Name", ["Name"]);
                mobs.Add(mob);
            }

            return mobs;
        }

        private void CopyHotspots(XElement element, XElement step)
        {
            foreach (XElement child in element.Elements())
            {
                string local = child.Name.LocalName;

                if (local.Equals("Hotspot", StringComparison.OrdinalIgnoreCase))
                {
                    step.Add(Hotspot(child));
                    continue;
                }

                if (HonorbuddyVocabulary.HotspotContainers.Contains(local, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (XElement spot in child.Elements())
                    {
                        if (spot.Name.LocalName.Equals("Hotspot", StringComparison.OrdinalIgnoreCase))
                        {
                            step.Add(Hotspot(spot));
                        }
                        else
                        {
                            Note(spot, $"<{spot.Name.LocalName}> inside <{local}> is not a hotspot.");
                        }
                    }

                    continue;
                }

                Note(child, $"<{local}> inside <{element.Name.LocalName}> is not something the importer knows.");
            }
        }

        private static XElement Hotspot(XElement element)
        {
            XElement hotspot = new("Hotspot");

            foreach (string axis in (string[])["X", "Y", "Z"])
            {
                if (AttributeOf(element, [axis]) is { } value)
                {
                    hotspot.SetAttributeValue(axis, value);
                }
            }

            return hotspot;
        }

        private void CopyCondition(XElement element, XElement step)
        {
            if (ConditionOf(element) is { } condition)
            {
                step.SetAttributeValue("Condition", condition);
            }
        }

        /// <summary>
        /// Translates the element's condition, counting what happened.
        /// </summary>
        /// <returns>The translated condition, or null when there was none or it was lost.</returns>
        private string? ConditionOf(XElement element)
        {
            string? text = AttributeOf(element, HonorbuddyVocabulary.ConditionAttributes);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            ConditionTranslation translation = HonorbuddyConditionTranslator.Translate(text);

            if (translation.Understood)
            {
                ConditionsConverted++;
                return translation.Translated;
            }

            ConditionsLost++;

            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Warning,
                $"The condition \"{translation.Original}\" could not be translated: {translation.Problem} "
                + "The step was kept but now runs unconditionally — check it before using this profile.",
                LineOf(element),
                element.Name.LocalName));

            return null;
        }

        private void CopyPosition(XElement element, XElement step)
        {
            foreach (string axis in (string[])["X", "Y", "Z"])
            {
                if (AttributeOf(element, [axis]) is { } value)
                {
                    step.SetAttributeValue(axis, value);
                }
            }
        }

        private static void CopyId(XElement element, XElement step, string name, string[] candidates)
        {
            if (AttributeOf(element, candidates) is { } value && value.Length > 0)
            {
                step.SetAttributeValue(name, value);
            }
        }

        private static void CopyText(XElement element, XElement step, string name, string[] candidates) =>
            CopyId(element, step, name, candidates);

        private static void CopyNumber(XElement element, XElement step, string name, string[] candidates) =>
            CopyId(element, step, name, candidates);

        private static string? AttributeOf(XElement element, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                XAttribute? attribute = element.Attributes().FirstOrDefault(a =>
                    a.Name.LocalName.Equals(candidate, StringComparison.OrdinalIgnoreCase));

                if (attribute is not null)
                {
                    return attribute.Value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Reports attributes the importer read nothing from.
        /// </summary>
        /// <remarks>
        /// These are where a conversion quietly loses behaviour, so they are worth a line each
        /// even though most of them turn out to be harmless.
        /// </remarks>
        private void ReportUnknownAttributes(XElement element)
        {
            string[] known =
            [
                .. HonorbuddyVocabulary.QuestIdAttributes,
                .. HonorbuddyVocabulary.QuestNameAttributes,
                .. HonorbuddyVocabulary.EntryAttributes,
                .. HonorbuddyVocabulary.ItemIdAttributes,
                .. HonorbuddyVocabulary.CountAttributes,
                .. HonorbuddyVocabulary.RadiusAttributes,
                .. HonorbuddyVocabulary.ObjectiveIndexAttributes,
                .. HonorbuddyVocabulary.ConditionAttributes,
                .. HonorbuddyVocabulary.IgnorableAttributes,
                "X", "Y", "Z",
            ];

            foreach (XAttribute attribute in element.Attributes())
            {
                string local = attribute.Name.LocalName;

                if (attribute.IsNamespaceDeclaration
                    || known.Contains(local, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                issues.Add(new ProfileIssue(
                    ProfileIssueSeverity.Warning,
                    $"'{local}' means nothing to this bot, so whatever it controlled was lost.",
                    LineOf(element),
                    element.Name.LocalName));
            }
        }

        /// <summary>Records a step the importer could not convert.</summary>
        private void Skip(XElement element, string message)
        {
            StepsNotUnderstood++;
            Note(element, message);
        }

        /// <summary>Records something worth mentioning that is not a lost step.</summary>
        private void Note(XElement element, string message) =>
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Warning,
                message,
                LineOf(element),
                element.Name.LocalName));

        private void Emit(XElement into, XElement step)
        {
            StepsConverted++;
            into.Add(step);
        }

        private static string? TextOf(XElement root, string name)
        {
            XElement? child = root.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (child is not null && !child.HasElements)
            {
                string value = child.Value.Trim();
                if (value.Length > 0)
                {
                    return value;
                }
            }

            return AttributeOf(root, [name]);
        }

        private static int LineOf(XObject node) =>
            node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
    }

    /// <summary>
    /// A <see cref="StringWriter"/> that says it is UTF-8.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> takes the encoding for the declaration from the writer it is
    /// given, and a plain <see cref="StringWriter"/> reports UTF-16. The result is then written
    /// to disk as UTF-8, so without this the file would carry a declaration that lies about
    /// itself.
    /// </remarks>
    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

}
