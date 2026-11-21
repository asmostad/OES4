# Pich — brand identity, draft 1

Direction F • 19 November 2026

## Identity

Pich is roofing and restoration business software. The identity should feel capable, clear, and approachable. Use the rounded, interlocking P selected in this conversation. Its diagonal channel is the signature; do not substitute a triangle, sharpen its corners, or enlarge the opening.

The primary lockup is Pich with a small, right-aligned ROOFING SOFTWARE descriptor and a single orange leading rule. The descriptor occupies approximately 75% of the wordmark's visible width. Its right edge aligns with the h. The rule begins at the left edge of the P, sits at the descriptor's mid-height, and stops before the R with a clear gap. No trailing rule.

## What this package contains

Twelve individual PNG concept exports on solid backgrounds, the approved reference, visual application concepts, a browsable brand book, exact color tokens, plain swatches, and generation prompts. The assets are generated raster drafts, not production vector masters. Small geometric differences can remain between exports. The PNGs should not be treated as exact ink-color or typography specifications; the values and type choices below are the intended production specifications. The initial transparent exports had visible halos and were excluded; final review files use white backgrounds, or navy backgrounds for the white marks.

Before manufacturing or final deployment, reconstruct and approve one vector P and one outlined wordmark, then derive every colorway from those same paths. This is especially important because previous generated sheets showed differences between the standalone P and the P in the wordmark. This package does not claim to contain that finished vector master.

## Logo family

| File in logos/ | Purpose |
|---|---|
| mark-color.png | Orange/navy standalone P on light surfaces |
| mark-navy.png | One-color P for light backgrounds and single-ink uses |
| mark-white.png | White P for dark backgrounds |
| circle-orange-white.png | Preferred badge: white P on orange circle |
| circle-navy-white.png | One-ink navy circle with white knocked-out P |
| circle-navy-orange.png | Alternate orange P on navy circle |
| wordmark-color.png | Pich without descriptor, on light backgrounds |
| wordmark-navy.png | Single-color wordmark |
| wordmark-white.png | White wordmark for dark backgrounds |
| lockup-color.png | Primary F lockup, including descriptor and orange rule |
| lockup-navy.png | Entire F lockup in one navy ink |
| lockup-white.png | Entire F lockup white for dark backgrounds |

White assets are supplied on navy backgrounds for review. In the future vector master, the channel should be negative space rather than an additional printed shape. The preferred circle badge contains two visible colors, but is reproducible as one orange ink with the P knocked out to white paper or substrate. These solid-background review PNGs are not drop-in transparent assets.

## Plain color specifications

| Color | HEX | RGB | Role |
|---|---|---|---|
| Pich Orange | #FF7900 | 255, 121, 0 | Signature cap, leading rule, badge background, accents |
| Pich Navy | #062536 | 6, 37, 54 | Wordmark, primary text, dark surfaces |
| White | #FFFFFF | 255, 255, 255 | Negative space and primary light background |
| Cloud | #F4F6F7 | 244, 246, 247 | Supporting neutral surface |
| Slate | #526570 | 82, 101, 112 | Secondary text |

Orange and navy are the core brand pair. Cloud and Slate are supporting proposed neutrals, not additional logo colors. Use solid fills rather than gradients. Keep most layouts white or navy with selective orange accents. HEX/RGB are the digital specifications; obtain printer-profile-specific CMYK conversions and a physical proof rather than treating a generic CMYK formula as a color match.

## Typography identities

**Pich wordmark: custom artwork.** The generated wordmark does not have an identified font name. Do not retype Pich in a substitute font and call it the same logo. Preserve the custom P, ich proportions, and optical spacing in final outlined artwork.

**Descriptor and occasional short display accents: Oxanium Medium (500), proposed production typeface.** Its squared geometry is a practical supporting choice for the direction A descriptor styling. This is a recommendation, not a claim that ImageGen used Oxanium. Begin with uppercase and approximately 0.08em tracking; adjust size and tracking to fit the approved 75% layout without horizontally stretching characters. Keep it quiet, not oversized or heavily tracked.

**Headings, body copy, product UI, and contact information: Inter.** Regular 400 for body, Medium 500 for labels, SemiBold 600 for headings, and Bold 700 sparingly. Use normal sentence case for prose and UI. A practical starting point is 16px/24px for web body and 10.5–11pt/15pt for printed correspondence. Use Arial as a fallback where the chosen fonts are unavailable.

Official font sources:
- Oxanium by Severin Meyer: https://github.com/sevmeyer/oxanium — SIL Open Font License.
- Inter by Rasmus Andersson: https://rsms.me/inter/ — SIL Open Font License.

The brand book links these sources. Font files are not bundled; font samples that cannot load use a labeled fallback. Raster logo lettering remains generated, not live typeset Oxanium/Inter.

## Layout and reproduction

- Clear space: start with 25% of the P's height around standalone marks, wordmarks, and lockups. Measure from visible artwork, not transparent file padding.
- Circle: center optically, with roughly 20% of the circle diameter as clear padding around the mark. Preserve the negative-space channel.
- Initial size targets, to proof: standalone P at least 24px high digitally or 6mm in print; wordmark at least 100px wide or 25mm; descriptor lockup at least 240px wide or 50mm. At smaller sizes omit the descriptor. These are starting targets, not validated production limits.
- Do not stretch, rotate, bevel, add shadows, close the diagonal channel, replace the P with a typed letter, or place the full wordmark beside a redundant P.
- Use white wordmark on navy. Use navy body text on white or orange. Reserve white-on-orange for the approved large logo badge; do not infer that it is suitable for small body text or interface labels. Check UI text contrast separately.
- Embroidery: use standalone mark or badge, omit the descriptor, and ask the vendor to proof the channel and stitch widths at actual size.

## Application drafts

| Application | Draft direction | Starting format |
|---|---|---|
| Business card front | White field, F lockup, navy name/contact block; orange leading line repeated sparingly | 3.5 × 2in trim, 0.125in bleed, 0.125in inner safe zone |
| Business card back | Navy field with preferred white-on-orange circle badge | Same trim; one focal point |
| Letterhead | F lockup top left, generous white body space, navy contact footer with short orange rule | US Letter, 8.5 × 11in; 0.75in text margins |
| Envelope | Compact color wordmark and return-address block at upper left | US #10, 9.5 × 4.125in; keep postal zones clear |
| Hat | Navy cap with white P on orange circular patch | Vendor-sized patch, roughly 2in diameter starting point |
| Polo | Small standalone white P or preferred badge on navy chest | No descriptor; vendor proof |
| App/social avatar | White P on orange badge; square orange field can be explored for OS icon masks | Export from master at platform-required sizes |
| Email signature | Small wordmark, person's name and role in live text, navy links, restrained orange rule | Compact responsive signature; no giant bitmap of all contact details |
| Proposal cover | Large F lockup, project title in Inter, navy footer and selective orange rule | US Letter; plenty of whitespace |

All contact details in application concepts are placeholders. Use hello@pich.example, pich.example, and +1 312 555 0100 only as mock content, not as claims of registered domains or live contact information. Replace before release.

## Handoff

The image prompts are saved in specifications/generation-prompts.json. Built-in ImageGen was used for raster logo exports and application mockups. Source reference and previews are preserved in previews/. Color tokens and swatches are deterministic companion specifications. No application source, project name, or live branding integration was changed.
