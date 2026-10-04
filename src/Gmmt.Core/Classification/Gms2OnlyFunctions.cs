using System.Collections.Frozen;

namespace Gmmt.Core.Classification;

/// <summary>
/// Canonical list of GMS2-only engine functions that do NOT exist in GMS1 runtime.
/// Shared between the classifier (pre-flight) and the transplanter (patching).
/// Code referencing these cannot be transplanted to a GMS1 target.
/// </summary>
public static class Gms2OnlyFunctions
{
    /// <summary>
    /// GMS2-only function names. If a code entry references any of these
    /// and the target is GMS1, the entry is not portable.
    /// </summary>
    public static readonly FrozenSet<string> Names = ((HashSet<string>)
    [
        // ── Layer system (GMS2 only) ──
        "layer_get_all", "layer_get_all_elements", "layer_get_name", "layer_get_depth",
        "layer_get_id", "layer_get_id_at_depth", "layer_get_element_layer", "layer_get_element_type",
        "layer_background_get_id", "layer_background_create", "layer_background_destroy",
        "layer_background_get_sprite", "layer_background_change", "layer_background_visible",
        "layer_background_get_visible", "layer_background_blend", "layer_background_get_blend",
        "layer_background_alpha", "layer_background_get_alpha", "layer_background_xscale",
        "layer_background_yscale", "layer_background_get_xscale", "layer_background_get_yscale",
        "layer_background_htiled", "layer_background_get_htiled", "layer_background_vtiled",
        "layer_background_get_vtiled", "layer_background_stretch", "layer_background_get_stretch",
        "layer_background_speed", "layer_background_get_speed", "layer_background_index",
        "layer_background_get_index",
        "layer_sprite_get_id", "layer_sprite_create", "layer_sprite_destroy", "layer_sprite_change",
        "layer_tilemap_create", "layer_tilemap_destroy", "layer_tilemap_get_id",
        "layer_instance_get_instance", "layer_create", "layer_destroy", "layer_depth",
        "layer_set_visible", "layer_get_visible", "layer_exists", "layer_x", "layer_y",
        "layer_get_x", "layer_get_y", "layer_hspeed", "layer_vspeed", "layer_get_hspeed",
        "layer_get_vspeed", "layer_script_begin", "layer_script_end",
        "layer_shader", "layer_get_shader", "layer_set_target_room", "layer_get_target_room",
        "layer_reset_target_room", "layer_force_draw_depth",
        "layer_is_draw_depth_forced", "layer_get_forced_depth",

        // ── View system (GMS2 internal) ──
        "__view_get", "__view_set",

        // ── Joystick (GMS2 internal) ──
        "__joystick_2_gamepad",

        // ── Camera system (GMS2 only) ──
        "camera_create", "camera_create_view", "camera_destroy", "camera_apply",
        "camera_get_view_x", "camera_get_view_y", "camera_get_view_width", "camera_get_view_height",
        "camera_set_view_pos", "camera_set_view_size", "camera_set_view_angle",
        "camera_set_view_target", "camera_get_view_target", "camera_get_active",
        "camera_set_begin_script", "camera_set_end_script", "camera_set_update_script",
        "view_get_camera", "view_set_camera",

        // ── Struct/method (GMS2.3+ only) ──
        "method", "is_method", "instanceof", "static_get", "static_set",
        "struct_get", "struct_set", "struct_exists", "struct_remove",
        "struct_get_names", "struct_names_count", "variable_struct_get", "variable_struct_set",
        "variable_struct_exists", "variable_struct_remove", "variable_struct_get_names",
        "variable_struct_names_count",

        // ── Exception handling (GMS2.3+) ──
        "exception_unhandled_handler", "try", "catch", "finally", "throw",

        // ── Tags / GC / Debug (GMS2-only) ──
        "tag_get_asset_ids", "tag_get_assets", "asset_get_tags", "asset_add_tags",
        "asset_remove_tags", "asset_has_tags", "asset_has_any_tag", "asset_clear_tags",
        "gc_collect", "gc_enable", "gc_is_enabled", "gc_get_stats",
        "dbg_view", "dbg_section", "dbg_text", "dbg_slider",
    ]).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Categorized GMS2-only function groups for diagnostic reporting.
    /// </summary>
    public static readonly Dictionary<string, string> CategoryByPrefix = new(StringComparer.Ordinal)
    {
        { "layer_", "GMS2 Layer System" },
        { "camera_", "GMS2 Camera System" },
        { "view_get_camera", "GMS2 Camera System" },
        { "view_set_camera", "GMS2 Camera System" },
        { "__view_", "GMS2 View Internals" },
        { "__joystick_", "GMS2 Joystick Internals" },
        { "method", "GMS2.3+ Struct/Method" },
        { "is_method", "GMS2.3+ Struct/Method" },
        { "instanceof", "GMS2.3+ Struct/Method" },
        { "static_", "GMS2.3+ Struct/Method" },
        { "struct_", "GMS2.3+ Struct/Method" },
        { "variable_struct_", "GMS2.3+ Struct/Method" },
        { "exception_", "GMS2.3+ Exception Handling" },
        { "try", "GMS2.3+ Exception Handling" },
        { "catch", "GMS2.3+ Exception Handling" },
        { "finally", "GMS2.3+ Exception Handling" },
        { "throw", "GMS2.3+ Exception Handling" },
        { "tag_", "GMS2 Tags" },
        { "asset_get_tags", "GMS2 Tags" },
        { "asset_add_tags", "GMS2 Tags" },
        { "asset_remove_tags", "GMS2 Tags" },
        { "asset_has_tags", "GMS2 Tags" },
        { "asset_has_any_tag", "GMS2 Tags" },
        { "asset_clear_tags", "GMS2 Tags" },
        { "gc_", "GMS2 Garbage Collector" },
        { "dbg_", "GMS2 Debug" },
    };

    /// <summary>
    /// Get the GMS2 feature category for a function name, or null if not categorized.
    /// </summary>
    public static string? GetCategory(string functionName)
    {
        // Try exact match first
        if (CategoryByPrefix.TryGetValue(functionName, out var exact))
            return exact;

        // Try prefix match
        foreach (var (prefix, category) in CategoryByPrefix)
        {
            if (functionName.StartsWith(prefix, StringComparison.Ordinal))
                return category;
        }

        return Names.Contains(functionName) ? "GMS2-Only" : null;
    }
}
