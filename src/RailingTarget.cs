using Nivalis;
using UnityEngine;

namespace NivalisMods.Cigarette;

internal static class RailingTarget
{
    internal static bool TryTarget(PlayerCharacter character, out RaycastHit hit, out string reason)
    {
        var hands = character.Controller.HandsAnimator;
        reason = "No collider within the 1.5 m camera ray / environment layer mask";
        var eye = character.Controller.Camera.transform;
        if (!Physics.Raycast(eye.position, eye.forward, out hit, 1.5f, hands.environmentLayer, QueryTriggerInteraction.Ignore)) return false;
        if (IsReviewedPlayerBlocker(hit.collider))
        {
            // Only the three captured Hive Mall collision volumes may be looked through.
            // Select the next physical hit, not any railing behind intervening geometry.
            var nearest = float.PositiveInfinity;
            var found = false;
            foreach (var candidate in Physics.RaycastAll(eye.position, eye.forward, 1.5f, hands.environmentLayer, QueryTriggerInteraction.Ignore))
            {
                if (candidate.collider == null || candidate.collider.transform.IsChildOf(character.transform) ||
                    IsReviewedPlayerBlocker(candidate.collider) || candidate.distance >= nearest) continue;
                hit = candidate;
                nearest = candidate.distance;
                found = true;
            }
            if (!found) { reason = "No physical surface behind reviewed player blocker"; return false; }
        }
        if (hit.collider == null || hit.collider.transform.IsChildOf(character.transform)) return false;
        reason = "No recognized railing/fence name or reviewed scene/mesh match";
        if (!IsRecognizedRail(hit.collider)) return false;
        var maximum = Plugin.RailDistance.Value;
        maximum = float.IsFinite(maximum) ? Mathf.Clamp(maximum, 0.2f, 0.65f) : 0.45f;
        var horizontalDistance = Vector3.ProjectOnPlane(hit.point - character.transform.position, Vector3.up).magnitude;
        reason = "Beyond maximum horizontal activation distance";
        if (horizontalDistance > maximum) return false;
        // Require a nearby reachable-height top at the aimed railing, not a distant object behind it.
        reason = "Downward probe did not find a top surface";
        var baseY = character.transform.position.y;
        if (!Physics.Raycast(new Vector3(hit.point.x, baseY + 1.65f, hit.point.z), Vector3.down,
            out var top, 1.0f, hands.environmentLayer, QueryTriggerInteraction.Ignore)) return false;
        if (top.collider != hit.collider) { reason = "Top surface belongs to a different collider"; return false; }
        if (top.normal.y <= 0.85f) { reason = "Top surface is too steep"; return false; }
        if (top.point.y - baseY > 1.4f) { reason = "Top is above the 1.4 m height limit"; return false; }
        hit = top; // Pass the verified surface to hand placement, not the originally aimed front face.
        reason = "Eligible railing target";
        return true;
    }

    private static bool IsRecognizedRail(Collider collider)
    {
        var rail = false;
        for (var node = collider.transform; node != null; node = node.parent)
        {
            var name = node.name.ToLowerInvariant();
            if (name.Contains("railing") || name.Contains("balustr") || name.Contains("guardrail") || name.Contains("handrail") || name.Contains("fence")) { rail = true; break; }
        }
        if (!rail)
        {
            var mesh = collider.TryCast<MeshCollider>()?.sharedMesh;
            if (mesh == null) mesh = collider.GetComponent<MeshFilter>()?.sharedMesh;
            rail = mesh != null && IsReviewedRailMesh(collider.gameObject.scene.name, mesh.name);
            // Static batching gives this mesh a generated name; use its exact object hierarchy instead.
            if (!rail && collider.gameObject.scene.name == "15_Metro_Hub")
            {
                var node = collider.transform;
                rail = node.name == "Column_Beam (66)" && node.parent?.name == "Other" &&
                    node.parent.parent?.name == "StreetProps" && node.parent.parent.parent == null;
            }
            // Captured Seaside barriers use BoxColliders and a shared static-batch
            // render mesh. Match only these two reviewed objects, not that mesh.
            if (!rail && collider.gameObject.scene.name == "9_Seaside_Boardwalk" && collider.TryCast<BoxCollider>() != null)
            {
                var node = collider.transform;
                rail = (node.name == "Tech_Construction (8)" || node.name == "Tech_Construction (1)") &&
                    node.parent?.name == "Tech" && node.parent.parent?.name == "_Detail_Props" &&
                    node.parent.parent.parent == null;
            }
        }
        return rail;
    }

    private static bool IsReviewedPlayerBlocker(Collider collider)
    {
        if (collider == null || collider.gameObject.scene.name != "11_Hive_Mall") return false;
        var node = collider.transform;
        return (node.name == "Cube (16)" || node.name == "Cube (38)" || node.name == "Cube (22)") &&
            node.parent?.name == "_Collision Volumes" && node.parent.parent?.name == "_Player_Colliders" &&
            node.parent.parent.parent == null;
    }

    // Reviewed railings: these railings share collision meshes with the surrounding building.
    // Match the collider's scene (the player lives in _Global), never a session-local ID.
    // These identities only replace the name check; reach and top-surface checks still apply.
    private static bool IsReviewedRailMesh(string scene, string mesh) => (scene, mesh) switch
    {
        ("1_Lowtown", "Apartment_Lowtown_Patch_combined_combine_Walls_A_0") => true,
        ("1_Lowtown", "Ground_Office_2_1_Building_Part_2_17") => true,
        ("1_Lowtown", "Small_Apartment_3_combined_combine_building_1") => true,
        ("2_Meridian_Market", "Noodle_Bar_combined_combine_Stairs_0") => true,
        ("2_Meridian_Market", "Noodle_Bar_Floor_1_combined_combine_Floor_1_0") => true,
        ("3_Docks", "Small_Apartment_3_combined_combine_building_1") => true,
        ("3_Docks", "Waterfront_Hut_0") => true,
        ("18_Spire", "Futuristic_Building_With_Shops_combined_combine_Main2_1") => true,
        ("18_Spire", "Futuristic_Building_With_Shops_combined_combine_Main1_2") => true,
        ("18_Spire", "Parking_Lot_Parkinglot_Right_2") => true,
        ("18_Spire", "Floating_Platform_Shops_2_Stairs_3_1") => true,
        ("18_Spire", "Floating_Platform_Club_combined_combine_Platform_1") => true,
        ("14_Space_Port", "Inner_City_Building_With_Shops_Combined__floor_1_3_0") => true,
        ("14_Space_Port", "Parking_Lot_Parkinglot_Left_4") => true,
        ("14_Space_Port", "Fishing_Equipment_Store_combined_combine_Main_0") => true,
        ("15_Metro_Hub", "Space_Port_Stairs_Interior_60") => true,
        ("4_Central_Canyon", "Parking_Lot_Parkinglot_Left_4") => true,
        ("4_Central_Canyon", "Multi-Tenant_Building_external_stairs2_29") => true,
        ("4_Central_Canyon", "Japanese_Style_Building_2_Main_1floor_3_6") => true,
        ("11_Hive_Mall", "Inner_City_Building_With_Shops_Combined__floor_1_3_0") => true,
        ("9_Seaside_Boardwalk", "Parking_Lot_Parkinglot_Right_2") => true,
        ("9_Seaside_Boardwalk", "Floating_Platform_Shops_2_Stairs_3_1") => true,
        ("17_Calypso_Island", "Parking_Lot_Parkinglot_Left_4") => true,
        ("17_Calypso_Island", "Asian_Restaurant_Metal_Stairs_25") => true,
        ("17_Calypso_Island", "Floating_Platform_Shops_2_Stairs_3_1") => true,
        ("17_Calypso_Island", "Bungalow_combined_combine_Main_1") => true,
        ("10_Helix_Monumental", "Parking_Lot_Parkinglot_Left_4") => true,
        ("10_Helix_Monumental", "HongKong_Style_Corner_Building_combined_combine_Back_0") => true,
        ("10_Helix_Monumental", "Space_Port_Aditional_Assets_Bridge_Upper_Floor_Connection_10") => true,
        ("10_Helix_Monumental", "Space_Port_Aditional_Assets_Barrier_Single_4") => true,
        ("10_Helix_Monumental", "Floating_Platform_Shops_2_Stairs_3_1") => true,
        ("10_Helix_Monumental", "Walkable_Modular_Building_Fan_07_14") => true,
        ("10_Helix_Monumental", "Yakitori_Japanese_BBQ_Shop_combined_combine_Main_0") => true,
        ("8_Industrial_District", "Cargo_Ship_combined_combine_Main_2_0") => true,
        ("8_Industrial_District", "Office_Building_Combined__Main_1_3") => true,
        ("8_Industrial_District", "Container_Stack_combined_combine_Stairs_1_2") => true,
        ("7_Sewers", "Cyberpunk_Building_combined_combine_Walls_4_0") => true,
        ("7_Sewers", "Modular_Building_combined_combine_Stairs_Left_12") => true,
        ("13_Stacks", "HongKong_Style_Corner_Building_combined_combine_Back_0") => true,
        ("5_Oil_Rig", "Container_Stack_combined_combine_Stairs_1_2") => true,
        ("5_Oil_Rig", "Container_Stack_combined_combine_Stairs_4_3") => true,
        ("16_Mountain_Town", "Asian_Restaurant_Metal_Stairs_25") => true,
        ("16_Mountain_Town", "Building_08_Bar_Metal_Stairs_5") => true,
        ("3_Docks", "Parking_Lot_Parkinglot_Left_4") => true,
        ("3_Docks", "Building30_Rocks_Optimized_cora_structure01_0") => true,
        ("6_Eastern_Residential", "Inner_City_Building_With_Shops_floor_1_4_11") => true,
        ("6_Eastern_Residential", "Hong_Kong_Restaurant_combined_combine_Main_3_4") => true,
        ("6_Eastern_Residential", "Hong_Kong_Restaurant_combined_combine_Main_4_5") => true,
        ("6_Eastern_Residential", "Hong_Kong_Cluster_Rear_ladder_12") => true,
        ("6_Eastern_Residential", "Hong_Kong_Restaurant_combined_combine_Main_1_7") => true,
        ("6_Eastern_Residential", "Hong_Kong_Restaurant_combined_combine_Main_2_6") => true,
        ("6_Eastern_Residential", "Inner_City_Building_With_Shops_Combined__floor_1_3_0") => true,
        ("1_Lowtown", "Ground_Office_2_1_Building_Part_3_15") => true,
        ("1_Lowtown", "Parking_Lot_Parkinglot_Right_2") => true,
        ("17_Calypso_Island", "Hong_Kong_Cluster_Clothesline_2_8") => true,
        _ => false
    };
}
