// SPDX-License-Identifier: MIT
//
// Canvas programs: the concentric dials and the usage bars. Same geometry as
// Rings._paint() in the GNOME extension so both front-ends look alike.

use crate::usage::Tone;
use cosmic::iced::{Color, Point, Radians, Rectangle, Size, mouse};
use cosmic::widget::canvas::{self, Frame, Geometry, LineCap, Path, Stroke, path::Arc};

const TRACK: Color = Color { r: 1.0, g: 1.0, b: 1.0, a: 0.12 };

fn tone_color(tone: Tone, alpha: f32) -> Color {
    let (r, g, b) = tone.rgb();
    Color { r, g, b, a: alpha }
}

/// Outer ring = session window, inner ring = weekly window. Both fill
/// clockwise from the top as you use them; an empty ring means nothing used.
/// Colour follows the session window's severity.
#[derive(Debug, Clone, Copy)]
pub struct Rings {
    /// Fraction used, 0..1, or None for no data.
    pub session: Option<f32>,
    pub weekly: Option<f32>,
    pub tone: Tone,
}

impl Rings {
    pub fn offline() -> Self {
        Rings { session: None, weekly: None, tone: Tone::Stale }
    }
}

impl<Message> canvas::Program<Message, cosmic::Theme> for Rings {
    type State = ();

    fn draw(
        &self,
        _state: &Self::State,
        renderer: &cosmic::Renderer,
        _theme: &cosmic::Theme,
        bounds: Rectangle,
        _cursor: mouse::Cursor,
    ) -> Vec<Geometry> {
        let mut frame = Frame::new(renderer, bounds.size());
        let w = bounds.width.min(bounds.height);
        let c = frame.center();
        let outer_w = (w * 0.095).max(2.5);
        let inner_w = (w * 0.06).max(1.5);
        let r_outer = w / 2.0 - outer_w / 2.0 - 0.5;
        let r_inner = r_outer - outer_w / 2.0 - inner_w / 2.0 - 2.5;
        let top = -std::f32::consts::FRAC_PI_2;

        let mut ring = |radius: f32, width: f32, frac: Option<f32>, alpha: f32| {
            frame.stroke(
                &Path::circle(c, radius),
                Stroke::default().with_width(width).with_color(TRACK),
            );
            if let Some(f) = frac.filter(|f| *f > 0.0) {
                let sweep = std::f32::consts::TAU * f.min(1.0);
                let arc = Path::new(|p| {
                    p.arc(Arc {
                        center: c,
                        radius,
                        start_angle: Radians(top),
                        end_angle: Radians(top + sweep),
                    });
                });
                frame.stroke(
                    &arc,
                    Stroke::default()
                        .with_width(width)
                        .with_color(tone_color(self.tone, alpha))
                        .with_line_cap(LineCap::Round),
                );
            }
        };

        ring(r_outer, outer_w, self.session, 1.0);
        if self.weekly.is_some() || r_inner > 3.0 {
            ring(r_inner, inner_w, self.weekly, 0.55);
        }

        vec![frame.into_geometry()]
    }
}

/// A rounded bar that fills left-to-right with percent used.
#[derive(Debug, Clone, Copy)]
pub struct Bar {
    pub used: u8,
    pub tone: Tone,
}

impl<Message> canvas::Program<Message, cosmic::Theme> for Bar {
    type State = ();

    fn draw(
        &self,
        _state: &Self::State,
        renderer: &cosmic::Renderer,
        _theme: &cosmic::Theme,
        bounds: Rectangle,
        _cursor: mouse::Cursor,
    ) -> Vec<Geometry> {
        let mut frame = Frame::new(renderer, bounds.size());
        let (w, h) = (bounds.width, bounds.height);
        let radius = h / 2.0;

        frame.fill(
            &Path::rounded_rectangle(Point::ORIGIN, Size::new(w, h), radius.into()),
            TRACK,
        );

        if self.used > 0 {
            let fill_w = (w * f32::from(self.used) / 100.0).max(h).min(w);
            frame.fill(
                &Path::rounded_rectangle(Point::ORIGIN, Size::new(fill_w, h), radius.into()),
                tone_color(self.tone, 1.0),
            );
        }

        vec![frame.into_geometry()]
    }
}
