using Microsoft.Build.Construction;

namespace NuGetToCentral;

public static class ProjectRootElementExtensions
{
    /// <summary>
    /// Convenience method that picks a location based on a heuristic:
    /// Finds first item group with no condition with at least one item of same type, or else an empty item group; or else adds a new item group;
    /// adds the item to that item group with items of the same type, ordered by include.
    /// Does not attempt to check whether the item matches an existing wildcard expression; that is only possible
    /// in the evaluated world.
    /// </summary>
    /// <param name="element">The element to add the item.</param>
    /// <param name="itemType">The type of element.</param>
    /// <param name="include">The items include metadata.</param>
    /// <param name="condition">The condition of the item group itself.</param>
    /// <param name="isFlatAttributeMetadata">Is the metadata a attribute.</param>
    /// <param name="metadata">The metadata for the item.</param>
    /// <returns>
    /// The new project item.
    /// </returns>
    /// <remarks>
    /// Per the previous implementation, it actually finds the last suitable item group, not the first.
    /// </remarks>
    public static ProjectItemElement AddItem(this ProjectRootElement element, string itemType, string include, string? condition, bool isFlatAttributeMetadata, params KeyValuePair<string, string>[]? metadata)
    {
        if (string.IsNullOrEmpty(itemType))
        {
            throw new ArgumentException($"'{nameof(itemType)}' cannot be null or empty.", nameof(itemType));
        }

        if (string.IsNullOrEmpty(include))
        {
            throw new ArgumentException($"'{nameof(include)}' cannot be null or empty.", nameof(include));
        }

        ProjectItemGroupElement? itemGroupToAddTo = null;

        foreach (var itemGroup in element.ItemGroups)
        {
            if (itemGroup.Condition.Length > 0)
            {
                continue;
            }

            if (itemGroupToAddTo == null && itemGroup.Count == 0)
            {
                itemGroupToAddTo = itemGroup;
            }

            if (!string.IsNullOrEmpty(condition) && !StringComparer.Ordinal.Equals(itemGroup.Condition, condition))
            {
                continue;
            }

            if (itemGroup.Items.All(item => StringComparer.OrdinalIgnoreCase.Equals(itemType, item.ItemType)))
            {
                itemGroupToAddTo = itemGroup;
            }

            if (itemGroupToAddTo?.Count > 0)
            {
                break;
            }
        }

        if (itemGroupToAddTo == null)
        {
            itemGroupToAddTo = element.AddItemGroup();
            itemGroupToAddTo.Condition = condition;
        }

        // If reference is null, this will prepend
        var newItem = itemGroupToAddTo.AddItem(itemType, include);

        if (metadata is null)
        {
            return newItem;
        }
        
        foreach (var metaDataItem in metadata)
        {
            newItem.AddMetadata(metaDataItem.Key, metaDataItem.Value, isFlatAttributeMetadata);
        }

        return newItem;
    }

    /// <summary>
    /// Convenience method that picks a location based on a heuristic.
    /// Updates the last existing property with the specified name that has no condition on itself or its property group, if any.
    /// Otherwise, adds a new property in the first property group without a condition, creating a property group if necessary after
    /// the last existing property group, else at the start of the project.
    /// </summary>
    /// <param name="item">The item to add to.</param>
    /// <param name="name">The name of the value.</param>
    /// <param name="value">The value of the value.</param>
    /// <param name="condition">The condition.</param>
    /// <returns>The new ProjectPropertyElement.</returns>
    public static ProjectPropertyElement AddProperty(this ProjectRootElement item, string name, string value, string? condition)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException($"'{nameof(name)}' cannot be null or empty.", nameof(name));
        }

        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"'{nameof(value)}' cannot be null or empty.", nameof(value));
        }

        ProjectPropertyGroupElement? matchingPropertyGroup = null;
        ProjectPropertyElement? matchingProperty = null;

        foreach (var propertyGroup in item.PropertyGroups)
        {
            if (propertyGroup.Condition.Length > 0)
            {
                continue;
            }

            matchingPropertyGroup ??= propertyGroup;

            if (!string.IsNullOrEmpty(condition) && !StringComparer.Ordinal.Equals(propertyGroup.Condition, condition))
            {
                continue;
            }

            foreach (var property in propertyGroup.Properties)
            {
                if (property.Condition.Length > 0)
                {
                    continue;
                }

                if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, name))
                {
                    matchingProperty = property;
                }
            }         
        }

        if (matchingProperty is not null)
        {
            matchingProperty.Value = value;

            return matchingProperty;
        }

        if (matchingPropertyGroup == null)
        {
            matchingPropertyGroup = item.AddPropertyGroup();
            matchingPropertyGroup.Condition = condition;
        }

        var newProperty = matchingPropertyGroup.AddProperty(name, value);

        return newProperty;
    }

    /// <summary>
    /// Convenience method that picks a location based on a heuristic:
    /// Finds first item group with no condition with at least one item of same type, or else an empty item group; or else adds a new item group;
    /// adds the item to that item group with items of the same type, ordered by include.
    /// Does not attempt to check whether the item matches an existing wildcard expression; that is only possible
    /// in the evaluated world.
    /// </summary>
    /// <param name="element">The element to add the item.</param>
    /// <param name="itemType">The type of element.</param>
    /// <param name="include">The items include metadata.</param>
    /// <param name="isFlatAttributeMetadata">Is the metadata a attribute.</param>
    /// <param name="metadata">The metadata for the item.</param>
    /// <returns>
    /// The new project item.
    /// </returns>
    /// <remarks>
    /// Per the previous implementation, it actually finds the last suitable item group, not the first.
    /// </remarks>
    public static ProjectItemElement AddItem(this ProjectRootElement element, string itemType, string include, bool isFlatAttributeMetadata, params KeyValuePair<string, string>[]? metadata)
    {
        ArgumentNullException.ThrowIfNull(itemType);

        ArgumentNullException.ThrowIfNull(include);

        ProjectItemGroupElement? itemGroupToAddTo = null;

        foreach (var itemGroup in element.ItemGroups)
        {
            if (itemGroup.Condition.Length > 0)
            {
                continue;
            }

            if (itemGroupToAddTo == null && itemGroup.Count == 0)
            {
                itemGroupToAddTo = itemGroup;
            }

            if (itemGroup.Items.All(item => StringComparer.OrdinalIgnoreCase.Equals(itemType, item.ItemType)))
            {
                itemGroupToAddTo = itemGroup;
            }

            if (itemGroupToAddTo?.Count > 0)
            {
                break;
            }
        }

        itemGroupToAddTo ??= element.AddItemGroup();

        // If reference is null, this will prepend
        var newItem = itemGroupToAddTo.AddItem(itemType, include);

        if (metadata is null)
        {
            return newItem;
        }
        
        foreach (var metadataItem in metadata)
        {
            newItem.AddMetadata(metadataItem.Key, metadataItem.Value, isFlatAttributeMetadata);
        }

        return newItem;
    }
}
