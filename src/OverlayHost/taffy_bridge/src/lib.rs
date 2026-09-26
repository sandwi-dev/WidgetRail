//! Narrow product-owned C ABI around the pinned Taffy layout engine.

use std::collections::{HashMap, HashSet};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::slice;
use taffy::geometry::{Point, Rect, Size};
use taffy::prelude::*;
use taffy::style::Overflow;

const ABI_VERSION: u32 = 5;
const OK: i32 = 0;
const INVALID_ARGUMENT: i32 = 1;
const INVALID_TREE: i32 = 2;
const LAYOUT_ERROR: i32 = 3;
const PANIC: i32 = 4;
const MAX_NODES: usize = 4096;

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct OptionalFloat {
    present: u32,
    value: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct Edges {
    top: f32,
    right: f32,
    bottom: f32,
    left: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct NodeInput {
    child_start: u32,
    child_count: u32,
    layout_mode: u32,
    direction: u32,
    wrap: u32,
    main_alignment: u32,
    cross_alignment: u32,
    overflow: u32,
    width: OptionalFloat,
    height: OptionalFloat,
    min_width: OptionalFloat,
    min_height: OptionalFloat,
    max_width: OptionalFloat,
    max_height: OptionalFloat,
    flex_basis: OptionalFloat,
    aspect_ratio: OptionalFloat,
    padding: Edges,
    margin: Edges,
    column_gap: f32,
    row_gap: f32,
    flex_grow: f32,
    flex_shrink: f32,
    grid_minimum_column_width: f32,
    grid_maximum_columns: u32,
    grid_start_index: i32,
    stretch_cross_axis: u32,
    width_fraction: OptionalFloat,
    stable_key: u64,
    measure_revision: u64,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct MeasureInput {
    known_width: OptionalFloat,
    known_height: OptionalFloat,
    available_width: f32,
    available_height: f32,
    available_width_mode: u32,
    available_height_mode: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct MeasuredSize {
    width: f32,
    height: f32,
}

pub type MeasureCallback =
    unsafe extern "C" fn(*mut core::ffi::c_void, u32, MeasureInput) -> MeasuredSize;

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct NodeOutput {
    x: f32,
    y: f32,
    width: f32,
    height: f32,
    content_width: f32,
    content_height: f32,
    padding: Edges,
}

#[unsafe(no_mangle)]
pub extern "C" fn wrail_taffy_abi_version() -> u32 {
    ABI_VERSION
}

fn dimension(value: OptionalFloat) -> Dimension {
    if value.present != 0 {
        length(value.value)
    } else {
        auto()
    }
}

fn optional_value(value: OptionalFloat) -> Option<f32> {
    (value.present != 0).then_some(value.value)
}

fn edges_length(value: Edges) -> Rect<LengthPercentage> {
    Rect {
        left: length(value.left),
        right: length(value.right),
        top: length(value.top),
        bottom: length(value.bottom),
    }
}

fn edges_auto(value: Edges) -> Rect<LengthPercentageAuto> {
    Rect {
        left: length(value.left),
        right: length(value.right),
        top: length(value.top),
        bottom: length(value.bottom),
    }
}

fn main_alignment(value: u32) -> Option<JustifyContent> {
    Some(match value {
        0 => JustifyContent::START,
        1 => JustifyContent::CENTER,
        2 => JustifyContent::END,
        3 => JustifyContent::SPACE_BETWEEN,
        4 => JustifyContent::SPACE_AROUND,
        _ => return None,
    })
}

fn cross_alignment(value: u32) -> Option<AlignItems> {
    Some(match value {
        0 => AlignItems::START,
        1 => AlignItems::CENTER,
        2 => AlignItems::END,
        3 => AlignItems::STRETCH,
        _ => return None,
    })
}

fn node_style(input: &NodeInput) -> Option<Style> {
    if input.width_fraction.present != 0
        && (input.width.present != 0
            || !input.width_fraction.value.is_finite()
            || input.width_fraction.value < 0.0)
    {
        return None;
    }
    let display = match input.layout_mode {
        0 => Display::Flex,
        1 => Display::Grid,
        _ => return None,
    };
    let direction = match input.direction {
        0 => FlexDirection::Row,
        1 => FlexDirection::Column,
        _ => return None,
    };
    let wrap = match input.wrap {
        0 => FlexWrap::NoWrap,
        1 => FlexWrap::Wrap,
        _ => return None,
    };
    let overflow = match input.overflow {
        0 => Overflow::Visible,
        1 => Overflow::Clip,
        _ => return None,
    };
    let align_items = cross_alignment(input.cross_alignment)?;
    let align_self = if input.stretch_cross_axis != 0 && input.aspect_ratio.present == 0 {
        None
    } else {
        Some(AlignSelf::START)
    };
    Some(Style {
        display,
        flex_direction: direction,
        flex_wrap: wrap,
        justify_content: main_alignment(input.main_alignment),
        align_content: (input.layout_mode == 1 || input.wrap == 1).then_some(AlignContent::START),
        align_items: Some(align_items),
        align_self,
        overflow: Point {
            x: overflow,
            y: overflow,
        },
        size: Size {
            width: if input.width_fraction.present != 0 {
                percent(input.width_fraction.value)
            } else {
                dimension(input.width)
            },
            height: dimension(input.height),
        },
        min_size: Size {
            width: dimension(input.min_width),
            height: dimension(input.min_height),
        },
        max_size: Size {
            width: dimension(input.max_width),
            height: dimension(input.max_height),
        },
        aspect_ratio: optional_value(input.aspect_ratio),
        padding: edges_length(input.padding),
        margin: edges_auto(input.margin),
        gap: Size {
            width: length(input.column_gap),
            height: length(input.row_gap),
        },
        flex_basis: dimension(input.flex_basis),
        flex_grow: input.flex_grow,
        flex_shrink: input.flex_shrink,
        ..Style::default()
    })
}

fn encode_available(value: AvailableSpace) -> (f32, u32) {
    match value {
        AvailableSpace::Definite(value) => (value, 0),
        AvailableSpace::MinContent => (0.0, 1),
        AvailableSpace::MaxContent => (0.0, 2),
    }
}

pub struct LayoutSession {
    tree: TaffyTree<u32>,
    nodes: HashMap<u64, (NodeId, u64)>,
}

impl Default for LayoutSession {
    fn default() -> Self {
        Self {
            tree: TaffyTree::new(),
            nodes: HashMap::new(),
        }
    }
}

impl LayoutSession {
    fn reset(&mut self) {
        *self = Self::default();
    }
}

#[allow(clippy::too_many_arguments)] // Mirrors the checked-in bulk C ABI.
fn compute_impl(
    inputs: &[NodeInput],
    child_indices: &[u32],
    root_index: u32,
    available_width: f32,
    available_height: f32,
    available_height_mode: u32,
    measure: Option<MeasureCallback>,
    measure_context: *mut core::ffi::c_void,
    outputs: &mut [NodeOutput],
) -> i32 {
    compute_retained(
        &mut LayoutSession::default(),
        inputs,
        child_indices,
        root_index,
        available_width,
        available_height,
        available_height_mode,
        measure,
        measure_context,
        outputs,
    )
}

#[allow(clippy::too_many_arguments)]
fn compute_retained(
    session: &mut LayoutSession,
    inputs: &[NodeInput],
    child_indices: &[u32],
    root_index: u32,
    available_width: f32,
    available_height: f32,
    available_height_mode: u32,
    measure: Option<MeasureCallback>,
    measure_context: *mut core::ffi::c_void,
    outputs: &mut [NodeOutput],
) -> i32 {
    if inputs.is_empty()
        || inputs.len() > MAX_NODES
        || outputs.len() < inputs.len()
        || root_index as usize >= inputs.len()
    {
        return INVALID_ARGUMENT;
    }
    if !available_width.is_finite()
        || !available_height.is_finite()
        || available_width < 0.0
        || available_height < 0.0
    {
        return INVALID_ARGUMENT;
    }

    // Validate the complete new graph before touching retained state. Parent
    // counts alone do not reject a disconnected cycle.
    let mut styles = Vec::with_capacity(inputs.len());
    let mut keys = HashSet::with_capacity(inputs.len());
    let mut parent_counts = vec![0u8; inputs.len()];
    for (index, input) in inputs.iter().enumerate() {
        let Some(style) = node_style(input) else {
            return INVALID_ARGUMENT;
        };
        styles.push(style);
        let key = if input.stable_key == 0 {
            index as u64 + 1
        } else {
            input.stable_key
        };
        if !keys.insert(key) {
            return INVALID_TREE;
        }
        let start = input.child_start as usize;
        let Some(end) = start.checked_add(input.child_count as usize) else {
            return INVALID_TREE;
        };
        if end > child_indices.len() {
            return INVALID_TREE;
        }
        for &child in &child_indices[start..end] {
            let child = child as usize;
            if child >= inputs.len() || child == index {
                return INVALID_TREE;
            }
            parent_counts[child] = parent_counts[child].saturating_add(1);
            if parent_counts[child] != 1 {
                return INVALID_TREE;
            }
        }
    }
    if parent_counts[root_index as usize] != 0
        || parent_counts
            .iter()
            .enumerate()
            .any(|(i, count)| i != root_index as usize && *count != 1)
    {
        return INVALID_TREE;
    }
    let mut visited = vec![false; inputs.len()];
    let mut pending = vec![root_index as usize];
    while let Some(index) = pending.pop() {
        if visited[index] {
            return INVALID_TREE;
        }
        visited[index] = true;
        let input = &inputs[index];
        pending.extend(
            child_indices[input.child_start as usize
                ..input.child_start as usize + input.child_count as usize]
                .iter()
                .map(|&i| i as usize),
        );
    }
    if visited.iter().any(|&v| !v) {
        return INVALID_TREE;
    }

    let tree = &mut session.tree;
    tree.disable_rounding();
    // Remove dead nodes before adding the new page: storage is bounded by the
    // admitted tree, not the number of pages the user has ever visited.
    let removed: Vec<_> = session
        .nodes
        .keys()
        .filter(|key| !keys.contains(key))
        .copied()
        .collect();
    for key in removed {
        let (node, _) = session.nodes.remove(&key).unwrap();
        // Taffy remove does not itself invalidate a surviving parent.
        if let Some(parent) = tree.parent(node)
            && tree.mark_dirty(parent).is_err()
        {
            return LAYOUT_ERROR;
        }
        // Explicitly retire the measure context as well as topology.
        if tree.set_node_context(node, None).is_err() || tree.remove(node).is_err() {
            return LAYOUT_ERROR;
        }
    }
    let mut node_ids = Vec::with_capacity(inputs.len());
    for (index, input) in inputs.iter().enumerate() {
        let key = if input.stable_key == 0 {
            index as u64 + 1
        } else {
            input.stable_key
        };
        let incoming_revision = if input.stable_key == 0 {
            0
        } else {
            input.measure_revision
        };
        let node = if let Some((node, revision)) = session.nodes.get_mut(&key) {
            // Context is only the index in this call's validated buffer. Updating
            // it must not invalidate otherwise unchanged intrinsic measurements.
            *tree.get_node_context_mut(*node).unwrap() = index as u32;
            if (incoming_revision == 0 || *revision != incoming_revision)
                && tree.mark_dirty(*node).is_err()
            {
                return LAYOUT_ERROR;
            }
            *revision = incoming_revision;
            if tree.style(*node).ok() != Some(&styles[index])
                && tree.set_style(*node, styles[index].clone()).is_err()
            {
                return LAYOUT_ERROR;
            }
            *node
        } else {
            let Ok(node) = tree.new_leaf_with_context(styles[index].clone(), index as u32) else {
                return LAYOUT_ERROR;
            };
            session.nodes.insert(key, (node, incoming_revision));
            node
        };
        node_ids.push(node);
    }
    // Detach changed relationships first. This prevents a transient cycle
    // when valid snapshots reparent/reorder existing nodes.
    let mut changed = Vec::new();
    for (index, input) in inputs.iter().enumerate() {
        let children: Vec<_> = child_indices
            [input.child_start as usize..input.child_start as usize + input.child_count as usize]
            .iter()
            .map(|&i| node_ids[i as usize])
            .collect();
        if tree.children(node_ids[index]).ok().as_ref() != Some(&children) {
            changed.push((index, children));
        }
    }
    for (index, _) in &changed {
        if tree.set_children(node_ids[*index], &[]).is_err() {
            return INVALID_TREE;
        }
    }
    for (index, children) in changed {
        if tree.set_children(node_ids[index], &children).is_err() {
            return INVALID_TREE;
        }
    }
    let root = node_ids[root_index as usize];
    let available_height = match available_height_mode {
        0 => AvailableSpace::Definite(available_height),
        1 => AvailableSpace::MinContent,
        2 => AvailableSpace::MaxContent,
        _ => return INVALID_ARGUMENT,
    };
    let available_space = Size {
        width: AvailableSpace::Definite(available_width),
        height: available_height,
    };

    let run_layout = |tree: &mut TaffyTree<u32>| {
        tree.compute_layout_with_measure(
            root,
            available_space,
            |known, available_space, _node_id, context, _style| {
                let Some(callback) = measure else {
                    return Size::ZERO;
                };
                let Some(index) = context.map(|value| *value) else {
                    return Size::ZERO;
                };
                let (available_width, available_width_mode) =
                    encode_available(available_space.width);
                let (available_height, available_height_mode) =
                    encode_available(available_space.height);
                let input = MeasureInput {
                    known_width: OptionalFloat {
                        present: known.width.is_some() as u32,
                        value: known.width.unwrap_or(0.0),
                    },
                    known_height: OptionalFloat {
                        present: known.height.is_some() as u32,
                        value: known.height.unwrap_or(0.0),
                    },
                    available_width,
                    available_height,
                    available_width_mode,
                    available_height_mode,
                };
                // SAFETY: the C++ caller owns the callback and context for the synchronous call.
                let measured = unsafe { callback(measure_context, index, input) };
                Size {
                    width: if measured.width.is_finite() {
                        measured.width.max(0.0)
                    } else {
                        0.0
                    },
                    height: if measured.height.is_finite() {
                        measured.height.max(0.0)
                    } else {
                        0.0
                    },
                }
            },
        )
    };

    if run_layout(tree).is_err() {
        return LAYOUT_ERROR;
    }

    // Responsive grids need their actual content width before their explicit,
    // capped track count can be authored. Resolve that generically, then let
    // Taffy perform the final CSS Grid sizing and placement.
    for _ in 0..3 {
        let mut changed = false;
        for (index, input) in inputs.iter().enumerate() {
            if input.layout_mode != 1 {
                continue;
            }
            let layout = tree.unrounded_layout(node_ids[index]);
            let content_width =
                (layout.size.width - layout.padding.left - layout.padding.right).max(0.0);
            let minimum = input.grid_minimum_column_width;
            if !minimum.is_finite() || minimum <= 0.0 {
                return INVALID_ARGUMENT;
            }
            let gap = input.column_gap.max(0.0);
            let natural = (((content_width + gap) / (minimum + gap)).floor() as u32).max(1);
            let maximum = input.grid_maximum_columns.max(1);
            let columns = natural.min(maximum).min(32) as u16;
            // Place the first retained item at its logical column. Remaining
            // children retain normal row-major auto placement.
            if input.child_count > 0 {
                let first = child_indices[input.child_start as usize] as usize;
                let column = input.grid_start_index.rem_euclid(columns as i32) as i16 + 1;
                let placement = Line {
                    start: line(column),
                    end: span(1),
                };
                if styles[first].grid_column != placement {
                    styles[first].grid_column = placement;
                    if tree
                        .set_style(node_ids[first], styles[first].clone())
                        .is_err()
                    {
                        return LAYOUT_ERROR;
                    }
                    changed = true;
                }
            }
            let effective_minimum = minimum.min(content_width);
            let tracks = vec![repeat(
                columns,
                vec![minmax(length(effective_minimum), fr(1.0_f32))],
            )];
            if styles[index].grid_template_columns != tracks {
                styles[index].grid_template_columns = tracks;
                if tree
                    .set_style(node_ids[index], styles[index].clone())
                    .is_err()
                {
                    return LAYOUT_ERROR;
                }
                changed = true;
            }
        }
        if !changed {
            break;
        }
        if run_layout(tree).is_err() {
            return LAYOUT_ERROR;
        }
    }

    for (index, output) in outputs.iter_mut().take(inputs.len()).enumerate() {
        let layout = tree.unrounded_layout(node_ids[index]);
        *output = NodeOutput {
            x: layout.location.x,
            y: layout.location.y,
            width: layout.size.width,
            height: layout.size.height,
            content_width: layout.content_size.width,
            content_height: layout.content_size.height,
            padding: Edges {
                top: layout.padding.top,
                right: layout.padding.right,
                bottom: layout.padding.bottom,
                left: layout.padding.left,
            },
        };
    }
    OK
}

#[unsafe(no_mangle)]
/// Computes one complete layout tree into the caller-owned output buffer.
///
/// # Safety
///
/// Every non-null pointer must address at least its paired count of properly
/// aligned ABI records and remain valid for this synchronous call. `outputs`
/// must be uniquely writable. The callback and its context, when supplied,
/// must remain valid and must not unwind across the C ABI.
pub unsafe extern "C" fn wrail_taffy_compute(
    nodes: *const NodeInput,
    node_count: usize,
    children: *const u32,
    child_count: usize,
    root_index: u32,
    available_width: f32,
    available_height: f32,
    available_height_mode: u32,
    measure: Option<MeasureCallback>,
    measure_context: *mut core::ffi::c_void,
    outputs: *mut NodeOutput,
    output_count: usize,
) -> i32 {
    if nodes.is_null() || outputs.is_null() || (child_count != 0 && children.is_null()) {
        return INVALID_ARGUMENT;
    }
    contain_panic(|| {
        // SAFETY: pointer/length pairs are checked for null above and remain
        // borrowed only for this synchronous call.
        let inputs = unsafe { slice::from_raw_parts(nodes, node_count) };
        let child_indices = if child_count == 0 {
            &[]
        } else {
            unsafe { slice::from_raw_parts(children, child_count) }
        };
        let output_slice = unsafe { slice::from_raw_parts_mut(outputs, output_count) };
        compute_impl(
            inputs,
            child_indices,
            root_index,
            available_width,
            available_height,
            available_height_mode,
            measure,
            measure_context,
            output_slice,
        )
    })
}

/// Opaque, single-owner layout state. No callback or caller buffer is retained.
#[unsafe(no_mangle)]
pub extern "C" fn wrail_taffy_session_create() -> *mut LayoutSession {
    catch_unwind(|| Box::into_raw(Box::new(LayoutSession::default())))
        .unwrap_or(core::ptr::null_mut())
}

/// # Safety
/// `session` must be null or a live handle returned by create, destroyed once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wrail_taffy_session_destroy(session: *mut LayoutSession) {
    if !session.is_null() {
        let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
            drop(Box::from_raw(session));
        }));
    }
}

/// # Safety
/// Same buffer/callback contract as wrail_taffy_compute. The handle must be
/// live and exclusively borrowed for this synchronous call; no reentrancy.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn wrail_taffy_session_compute(
    session: *mut LayoutSession,
    nodes: *const NodeInput,
    node_count: usize,
    children: *const u32,
    child_count: usize,
    root_index: u32,
    available_width: f32,
    available_height: f32,
    available_height_mode: u32,
    measure: Option<MeasureCallback>,
    measure_context: *mut core::ffi::c_void,
    outputs: *mut NodeOutput,
    output_count: usize,
) -> i32 {
    if session.is_null()
        || nodes.is_null()
        || outputs.is_null()
        || (child_count != 0 && children.is_null())
    {
        return INVALID_ARGUMENT;
    }
    let session = unsafe { &mut *session };
    let result = contain_panic(|| {
        let inputs = unsafe { slice::from_raw_parts(nodes, node_count) };
        let children = if child_count == 0 {
            &[]
        } else {
            unsafe { slice::from_raw_parts(children, child_count) }
        };
        let outputs = unsafe { slice::from_raw_parts_mut(outputs, output_count) };
        compute_retained(
            session,
            inputs,
            children,
            root_index,
            available_width,
            available_height,
            available_height_mode,
            measure,
            measure_context,
            outputs,
        )
    });
    // A failed admission cannot leave partially mutated state available for reuse.
    if result != OK {
        session.reset();
    }
    result
}

fn contain_panic(operation: impl FnOnce() -> i32) -> i32 {
    catch_unwind(AssertUnwindSafe(operation)).unwrap_or(PANIC)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn c_abi_sizes_are_stable() {
        assert_eq!(core::mem::size_of::<OptionalFloat>(), 8);
        assert_eq!(core::mem::size_of::<Edges>(), 16);
        assert_eq!(core::mem::size_of::<NodeInput>(), 184);
        assert_eq!(core::mem::size_of::<MeasureInput>(), 32);
        assert_eq!(core::mem::size_of::<MeasuredSize>(), 8);
        assert_eq!(core::mem::size_of::<NodeOutput>(), 40);
        assert_eq!(wrail_taffy_abi_version(), 5);
    }

    #[test]
    fn percentage_widths_are_typed_and_invalid_values_are_rejected() {
        let mut node = NodeInput {
            width_fraction: OptionalFloat {
                present: 1,
                value: 0.5,
            },
            ..NodeInput::default()
        };
        assert_eq!(node_style(&node).unwrap().size.width, percent(0.5_f32));
        for value in [f32::NAN, f32::INFINITY, -0.5] {
            node.width_fraction.value = value;
            assert!(node_style(&node).is_none());
        }
        node.width_fraction.value = 1.0;
        node.width = OptionalFloat {
            present: 1,
            value: 100.0,
        };
        assert!(node_style(&node).is_none());
    }

    #[test]
    fn invalid_flat_tree_is_rejected() {
        let mut nodes = vec![NodeInput::default(); 2];
        nodes[0].child_start = 0;
        nodes[0].child_count = 1;
        let mut outputs = vec![NodeOutput::default(); nodes.len()];
        assert_eq!(
            compute_impl(
                &nodes,
                &[0],
                0,
                640.0,
                480.0,
                0,
                None,
                core::ptr::null_mut(),
                &mut outputs,
            ),
            INVALID_TREE
        );
    }

    unsafe extern "C" fn fixed_measure(
        context: *mut core::ffi::c_void,
        node_index: u32,
        _input: MeasureInput,
    ) -> MeasuredSize {
        // SAFETY: the test passes a live usize for the synchronous layout call.
        unsafe { *(context.cast::<usize>()) += 1 };
        assert_eq!(node_index, 0);
        MeasuredSize {
            width: 123.0,
            height: 45.0,
        }
    }

    #[test]
    fn intrinsic_measurement_and_output_bounds_are_enforced() {
        let nodes = [NodeInput::default()];
        let mut calls = 0usize;
        let mut outputs = [NodeOutput::default()];
        assert_eq!(
            compute_impl(
                &nodes,
                &[],
                0,
                640.0,
                480.0,
                0,
                Some(fixed_measure),
                (&mut calls as *mut usize).cast(),
                &mut outputs,
            ),
            OK
        );
        assert!(calls > 0);
        assert!((outputs[0].width - 123.0).abs() < 0.01);
        assert!((outputs[0].height - 45.0).abs() < 0.01);
        assert_eq!(
            compute_impl(
                &nodes,
                &[],
                0,
                640.0,
                480.0,
                0,
                None,
                core::ptr::null_mut(),
                &mut [],
            ),
            INVALID_ARGUMENT
        );
    }

    #[test]
    fn panics_are_contained_as_error_codes() {
        assert_eq!(contain_panic(|| panic!("test panic")), PANIC);
    }

    #[test]
    fn abi_layouts_a_capped_grid() {
        let mut nodes = vec![NodeInput::default(); 5];
        nodes[0].child_count = 4;
        nodes[0].layout_mode = 1;
        nodes[0].grid_minimum_column_width = 100.0;
        nodes[0].grid_maximum_columns = 3;
        nodes[0].column_gap = 10.0;
        nodes[0].width = OptionalFloat {
            present: 1,
            value: 500.0,
        };
        nodes[0].height = OptionalFloat {
            present: 1,
            value: 200.0,
        };
        for node in &mut nodes[1..] {
            node.height = OptionalFloat {
                present: 1,
                value: 40.0,
            };
        }
        let children = [1, 2, 3, 4];
        let mut outputs = vec![NodeOutput::default(); nodes.len()];
        let result = compute_impl(
            &nodes,
            &children,
            0,
            500.0,
            200.0,
            0,
            None,
            core::ptr::null_mut(),
            &mut outputs,
        );
        assert_eq!(result, OK);
        assert!(
            (outputs[1].width - 160.0).abs() < 0.01,
            "first child width was {}",
            outputs[1].width
        );
        assert!(
            (outputs[2].x - 170.0).abs() < 0.01,
            "second child x was {}",
            outputs[2].x
        );
        assert!(outputs[4].y >= 40.0);
    }
    #[test]
    fn retained_measurement_is_invalidated_by_revision_and_eviction() {
        let mut session = LayoutSession::default();
        let mut node = NodeInput {
            stable_key: 42,
            measure_revision: 1,
            ..NodeInput::default()
        };
        let mut output = [NodeOutput::default()];
        let mut calls = 0usize;
        let run = |session: &mut LayoutSession,
                   node: NodeInput,
                   calls: &mut usize,
                   output: &mut [NodeOutput]| {
            compute_retained(
                session,
                &[node],
                &[],
                0,
                640.0,
                480.0,
                0,
                Some(fixed_measure),
                (calls as *mut usize).cast(),
                output,
            )
        };
        assert_eq!(run(&mut session, node, &mut calls, &mut output), OK);
        let first = calls;
        assert!(first > 0);
        assert_eq!(run(&mut session, node, &mut calls, &mut output), OK);
        assert_eq!(calls, first);
        node.measure_revision += 1;
        assert_eq!(run(&mut session, node, &mut calls, &mut output), OK);
        assert!(calls > first);
        for key in 50..150 {
            node.stable_key = key;
            assert_eq!(run(&mut session, node, &mut calls, &mut output), OK);
            assert_eq!(session.nodes.len(), 1);
            assert_eq!(session.tree.total_node_count(), 1);
        }
    }

    #[test]
    fn retained_grids_match_fresh_layout_through_window_changes() {
        let mut session = LayoutSession::default();
        for iteration in 0..120usize {
            let count = 5 + iteration % 17;
            let mut nodes = vec![NodeInput::default(); count + 1];
            let width = 521.25 - (iteration % 5) as f32 * 41.5;
            nodes[0] = NodeInput {
                stable_key: 1,
                measure_revision: 1,
                child_count: count as u32,
                layout_mode: 1,
                grid_minimum_column_width: 90.5,
                grid_maximum_columns: 6,
                grid_start_index: (iteration % 11) as i32,
                column_gap: 7.25,
                row_gap: 5.75,
                width: OptionalFloat {
                    present: 1,
                    value: width,
                },
                ..NodeInput::default()
            };
            for (i, node) in nodes.iter_mut().enumerate().skip(1) {
                node.stable_key = 10 + ((i + iteration / 3) % 40) as u64;
                node.measure_revision = 1;
                node.height = OptionalFloat {
                    present: 1,
                    value: 40.25 + (i % 3) as f32 * 8.5,
                };
            }
            let mut children: Vec<_> = (1..=count as u32).collect();
            if iteration % 2 == 1 {
                children.reverse();
            }
            let mut actual = vec![NodeOutput::default(); nodes.len()];
            let mut expected = actual.clone();
            assert_eq!(
                compute_retained(
                    &mut session,
                    &nodes,
                    &children,
                    0,
                    width,
                    480.0,
                    0,
                    None,
                    core::ptr::null_mut(),
                    &mut actual
                ),
                OK
            );
            assert_eq!(
                compute_impl(
                    &nodes,
                    &children,
                    0,
                    width,
                    480.0,
                    0,
                    None,
                    core::ptr::null_mut(),
                    &mut expected
                ),
                OK
            );
            for (a, b) in actual.iter().zip(&expected) {
                assert_eq!(
                    (
                        a.x,
                        a.y,
                        a.width,
                        a.height,
                        a.content_width,
                        a.content_height
                    ),
                    (
                        b.x,
                        b.y,
                        b.width,
                        b.height,
                        b.content_width,
                        b.content_height
                    ),
                    "iteration {iteration}"
                );
            }
            assert_eq!(session.nodes.len(), nodes.len());
        }
    }

    #[test]
    fn disconnected_cycle_is_rejected_before_retained_mutation() {
        let mut session = LayoutSession::default();
        let mut nodes = vec![NodeInput::default(); 3];
        nodes[1].child_count = 1;
        nodes[2].child_start = 1;
        nodes[2].child_count = 1;
        let mut outputs = vec![NodeOutput::default(); 3];
        assert_eq!(
            compute_retained(
                &mut session,
                &nodes,
                &[2, 1],
                0,
                100.0,
                100.0,
                0,
                None,
                core::ptr::null_mut(),
                &mut outputs
            ),
            INVALID_TREE
        );
        assert!(session.nodes.is_empty());
    }
    #[test]
    fn opaque_session_discards_failed_admission_and_recovers() {
        let handle = wrail_taffy_session_create();
        assert!(!handle.is_null());
        let mut node = NodeInput {
            stable_key: 42,
            measure_revision: 1,
            ..NodeInput::default()
        };
        let mut output = NodeOutput::default();
        let mut calls = 0usize;
        // SAFETY: all buffers and the exclusively owned handle remain live for
        // these synchronous calls and are destroyed exactly once below.
        unsafe {
            assert_eq!(
                wrail_taffy_session_compute(
                    handle,
                    &node,
                    1,
                    core::ptr::null(),
                    0,
                    0,
                    640.0,
                    480.0,
                    0,
                    Some(fixed_measure),
                    (&mut calls as *mut usize).cast(),
                    &mut output,
                    1
                ),
                OK
            );
            node.child_count = 1;
            assert_eq!(
                wrail_taffy_session_compute(
                    handle,
                    &node,
                    1,
                    &0,
                    1,
                    0,
                    640.0,
                    480.0,
                    0,
                    Some(fixed_measure),
                    (&mut calls as *mut usize).cast(),
                    &mut output,
                    1
                ),
                INVALID_TREE
            );
            assert!((*handle).nodes.is_empty());
            node.child_count = 0;
            let before = calls;
            assert_eq!(
                wrail_taffy_session_compute(
                    handle,
                    &node,
                    1,
                    core::ptr::null(),
                    0,
                    0,
                    640.0,
                    480.0,
                    0,
                    Some(fixed_measure),
                    (&mut calls as *mut usize).cast(),
                    &mut output,
                    1
                ),
                OK
            );
            assert!(calls > before);
            wrail_taffy_session_destroy(handle);
        }
    }
}
