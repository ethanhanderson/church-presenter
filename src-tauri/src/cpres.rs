//! .cpres bundle format handler
//!
//! A .cpres file is a ZIP archive containing:
//! - manifest.json: Presentation metadata and media manifest
//! - slides.json: Slide data
//! - arrangement.json: Slide ordering and section groups
//! - themes/*.json: Embedded themes
//! - media/*: Media files (images, videos, audio)

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs::{self, File};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use tempfile::NamedTempFile;
use thiserror::Error;
use zip::write::SimpleFileOptions;
use zip::{ZipArchive, ZipWriter};

#[derive(Error, Debug)]
pub enum CpresError {
    #[error("IO error: {0}")]
    Io(#[from] std::io::Error),

    #[error("ZIP error: {0}")]
    Zip(#[from] zip::result::ZipError),

    #[error("JSON error: {0}")]
    Json(#[from] serde_json::Error),

    #[error("Invalid bundle: {0}")]
    InvalidBundle(String),

    #[error("Missing file in bundle: {0}")]
    MissingFile(String),
}

impl Serialize for CpresError {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        serializer.serialize_str(&self.to_string())
    }
}

/// Parsed presentation bundle - contains raw JSON strings to be parsed by frontend
#[derive(Debug, Serialize, Deserialize)]
pub struct ParsedBundle {
    pub manifest: String,
    pub slides: String,
    pub arrangement: String,
    pub themes: Vec<ThemeFile>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ThemeFile {
    pub filename: String,
    pub content: String,
}

/// Media entry computed during import
#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct MediaEntry {
    pub id: String,
    pub filename: String,
    pub path: String,
    pub mime: String,
    pub sha256: String,
    pub byte_size: u64,
    pub media_type: String,
}

/// Bundle state for saving - contains raw JSON strings from frontend
#[derive(Debug, Serialize, Deserialize)]
pub struct BundleState {
    pub manifest: String,
    pub slides: String,
    pub arrangement: String,
    pub themes: Vec<ThemeFile>,
    pub media: Vec<MediaFileRef>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct MediaFileRef {
    pub id: String,
    pub source_path: String, // Absolute path to source file or "bundle:<path>" for existing
    pub bundle_path: String, // Path within the bundle (e.g., "media/abc123.jpg")
}

/// Open and parse a .cpres bundle
pub fn open_bundle(path: &Path) -> Result<ParsedBundle, CpresError> {
    let file = File::open(path)?;
    let mut archive = ZipArchive::new(file)?;

    // Read manifest.json
    let manifest = read_zip_file(&mut archive, "manifest.json")?;

    // Validate manifest has required fields
    let manifest_json: serde_json::Value = serde_json::from_str(&manifest)?;
    if !manifest_json.get("formatVersion").is_some() {
        return Err(CpresError::InvalidBundle(
            "Missing formatVersion in manifest".to_string(),
        ));
    }
    if !manifest_json.get("presentationId").is_some() {
        return Err(CpresError::InvalidBundle(
            "Missing presentationId in manifest".to_string(),
        ));
    }

    // Read slides.json
    let slides = read_zip_file(&mut archive, "slides.json")?;

    // Read arrangement.json
    let arrangement = read_zip_file(&mut archive, "arrangement.json")?;

    // Read all theme files
    let mut themes = Vec::new();
    for i in 0..archive.len() {
        let file = archive.by_index(i)?;
        let name = file.name().to_string();
        if name.starts_with("themes/") && name.ends_with(".json") {
            drop(file);
            let content = read_zip_file(&mut archive, &name)?;
            themes.push(ThemeFile {
                filename: name,
                content,
            });
        }
    }

    Ok(ParsedBundle {
        manifest,
        slides,
        arrangement,
        themes,
    })
}

/// Save a presentation bundle atomically (write to temp file, then rename)
pub fn save_bundle(path: &Path, state: &BundleState) -> Result<(), CpresError> {
    // Create temp file in the same directory for atomic rename
    let parent = path.parent().unwrap_or(Path::new("."));
    fs::create_dir_all(parent)?;

    let temp_file = NamedTempFile::new_in(parent)?;
    let file = temp_file.reopen()?;
    let mut zip = ZipWriter::new(file);
    let options = SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);

    // Write manifest.json
    zip.start_file("manifest.json", options)?;
    zip.write_all(state.manifest.as_bytes())?;

    // Write slides.json
    zip.start_file("slides.json", options)?;
    zip.write_all(state.slides.as_bytes())?;

    // Write arrangement.json
    zip.start_file("arrangement.json", options)?;
    zip.write_all(state.arrangement.as_bytes())?;

    // Write theme files
    for theme in &state.themes {
        zip.start_file(&theme.filename, options)?;
        zip.write_all(theme.content.as_bytes())?;
    }

    // Write media files
    for media_ref in &state.media {
        let source_data = if media_ref.source_path.starts_with("bundle:") {
            // Media is from an existing bundle - we need to handle this case
            // For now, skip - this would require keeping the original bundle open
            continue;
        } else {
            // Read from source file
            fs::read(&media_ref.source_path)?
        };

        zip.start_file(&media_ref.bundle_path, options)?;
        zip.write_all(&source_data)?;
    }

    zip.finish()?;

    // Atomic rename
    temp_file
        .persist(path)
        .map_err(|e| CpresError::Io(e.error))?;

    Ok(())
}

/// Read media file from a bundle as base64
pub fn read_bundle_media(bundle_path: &Path, media_path: &str) -> Result<Vec<u8>, CpresError> {
    let file = File::open(bundle_path)?;
    let mut archive = ZipArchive::new(file)?;

    let mut media_file = archive
        .by_name(media_path)
        .map_err(|_| CpresError::MissingFile(media_path.to_string()))?;

    let mut buffer = Vec::new();
    media_file.read_to_end(&mut buffer)?;

    Ok(buffer)
}

/// Import media files and compute their hashes
pub fn import_media_files(paths: &[PathBuf]) -> Result<Vec<MediaEntry>, CpresError> {
    let mut entries = Vec::new();

    for path in paths {
        let id = uuid::Uuid::new_v4().to_string();
        let filename = path
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or("unknown")
            .to_string();

        let extension = path
            .extension()
            .and_then(|e| e.to_str())
            .unwrap_or("")
            .to_lowercase();

        let mime = match extension.as_str() {
            "jpg" | "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "mov" => "video/quicktime",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "ogg" => "audio/ogg",
            _ => "application/octet-stream",
        }
        .to_string();

        let media_type = if mime.starts_with("image/") {
            "image"
        } else if mime.starts_with("video/") {
            "video"
        } else if mime.starts_with("audio/") {
            "audio"
        } else {
            "unknown"
        }
        .to_string();

        // Read file and compute hash
        let data = fs::read(path)?;
        let byte_size = data.len() as u64;

        let mut hasher = Sha256::new();
        hasher.update(&data);
        let sha256 = hex::encode(hasher.finalize());

        let bundle_path = format!("media/{}.{}", &id[..8], extension);

        entries.push(MediaEntry {
            id,
            filename,
            path: bundle_path,
            mime,
            sha256,
            byte_size,
            media_type,
        });
    }

    Ok(entries)
}

/// Helper to read a file from a ZIP archive as a string
fn read_zip_file(archive: &mut ZipArchive<File>, name: &str) -> Result<String, CpresError> {
    let mut file = archive
        .by_name(name)
        .map_err(|_| CpresError::MissingFile(name.to_string()))?;

    let mut contents = String::new();
    file.read_to_string(&mut contents)?;

    Ok(contents)
}
