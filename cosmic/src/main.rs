// SPDX-License-Identifier: MIT
//
// Codenotch for COSMIC: the same dials as the GNOME extension, as a panel
// applet. Reads usage JSON from a local `codexbar serve` over loopback and
// never touches provider credentials itself.

mod app;
mod draw;
mod usage;

fn main() -> cosmic::iced::Result {
    cosmic::applet::run::<app::Codenotch>(())
}
