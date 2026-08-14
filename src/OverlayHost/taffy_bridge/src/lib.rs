//! Narrow product-owned C ABI around the pinned Taffy layout engine.

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::slice;
use taffy::geometry::{Point, Rect, Size};
use taffy::prelude::*;
use taffy::style::Overflow;

const ABI_VERSION: u32 = 1;
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
    stretch_cross_axis: u32,
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
pub extern "C" fn gba_taffy_abi_version() -> u32 {
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
            width: dimension(input.width),
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

#[allow(clippy::too_many_arguments)] // Mirrors the checked-in bulk C ABI.
fn compute_impl(
    inputs: &[NodeInput],
    child_indices: &[u32],
    root_index: u32,
    available_width: f32,
    available_height: f32,
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

    let mut tree: TaffyTree<u32> = TaffyTree::with_capacity(inputs.len());
    tree.disable_rounding();
    let mut node_ids = Vec::with_capacity(inputs.len());
    let mut styles = Vec::with_capacity(inputs.len());
    for (index, input) in inputs.iter().enumerate() {
        let Some(style) = node_style(input) else {
            return INVALID_ARGUMENT;
        };
        let Ok(node) = tree.new_leaf_with_context(style.clone(), index as u32) else {
            return LAYOUT_ERROR;
        };
        node_ids.push(node);
        styles.push(style);
    }

    let mut parent_counts = vec![0u8; inputs.len()];
    for (index, input) in inputs.iter().enumerate() {
        let start = input.child_start as usize;
        let count = input.child_count as usize;
        let Some(end) = start.checked_add(count) else {
            return INVALID_TREE;
        };
        if end > child_indices.len() {
            return INVALID_TREE;
        }
        let mut children = Vec::with_capacity(count);
        for child_index in &child_indices[start..end] {
            let child = *child_index as usize;
            if child >= inputs.len() || child == index {
                return INVALID_TREE;
            }
            parent_counts[child] = parent_counts[child].saturating_add(1);
            if parent_counts[child] != 1 {
                return INVALID_TREE;
            }
            children.push(node_ids[child]);
        }
        if tree.set_children(node_ids[index], &children).is_err() {
            return INVALID_TREE;
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

    let root = node_ids[root_index as usize];
    let available_space = Size {
        width: AvailableSpace::Definite(available_width),
        height: AvailableSpace::Definite(available_height),
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

    if run_layout(&mut tree).is_err() {
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
        if run_layout(&mut tree).is_err() {
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
pub unsafe extern "C" fn gba_taffy_compute(
    nodes: *const NodeInput,
    node_count: usize,
    children: *const u32,
    child_count: usize,
    root_index: u32,
    available_width: f32,
    available_height: f32,
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
            measure,
            measure_context,
            output_slice,
        )
    })
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
        assert_eq!(core::mem::size_of::<NodeInput>(), 156);
        assert_eq!(core::mem::size_of::<MeasureInput>(), 32);
        assert_eq!(core::mem::size_of::<MeasuredSize>(), 8);
        assert_eq!(core::mem::size_of::<NodeOutput>(), 40);
        assert_eq!(gba_taffy_abi_version(), 1);
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
}
