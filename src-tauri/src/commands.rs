//! Tauri commands for the Church Presenter app

use crate::cpres::{self, BundleState, FontEntry, MediaEntry, ParsedBundle};
use font_kit::handle::Handle;
use font_kit::properties::Style;
use font_kit::source::SystemSource;
use std::path::{Path, PathBuf};
use tauri::{path::BaseDirectory, Manager};
use tauri_plugin_fs::FsExt;
#[cfg(target_os = "windows")]
use windows::core::PCWSTR;
#[cfg(target_os = "windows")]
use windows::Win32::Graphics::Gdi::{
    EnumDisplayDevicesW, EnumDisplaySettingsW, DEVMODEW, DISPLAY_DEVICEW, ENUM_CURRENT_SETTINGS,
};

/// Open a .cpres presentation bundle
#[tauri::command]
pub async fn cpres_open(path: String) -> Result<ParsedBundle, String> {
    let path = PathBuf::from(path);
    cpres::open_bundle(&path).map_err(|e| e.to_string())
}

/// Save a presentation bundle atomically
#[tauri::command]
pub async fn cpres_save(path: String, state: BundleState) -> Result<(), String> {
    let path = PathBuf::from(path);
    cpres::save_bundle(&path, &state).map_err(|e| e.to_string())
}

/// Read media from a bundle as base64
#[tauri::command]
pub async fn cpres_read_media(bundle_path: String, media_path: String) -> Result<Vec<u8>, String> {
    let path = PathBuf::from(bundle_path);
    cpres::read_bundle_media(&path, &media_path).map_err(|e| e.to_string())
}

/// Import media files and compute their metadata/hashes
#[tauri::command]
pub async fn cpres_import_media(paths: Vec<String>) -> Result<Vec<MediaEntry>, String> {
    let paths: Vec<PathBuf> = paths.into_iter().map(PathBuf::from).collect();
    cpres::import_media_files(&paths).map_err(|e| e.to_string())
}

/// Import font files and compute their metadata/hashes
#[tauri::command]
pub async fn cpres_import_fonts(paths: Vec<String>) -> Result<Vec<FontEntry>, String> {
    let paths: Vec<PathBuf> = paths.into_iter().map(PathBuf::from).collect();
    cpres::import_font_files(&paths).map_err(|e| e.to_string())
}

#[derive(serde::Serialize)]
pub struct SystemFontInfo {
    pub family: String,
    pub full_name: String,
    pub postscript_name: Option<String>,
    pub path: String,
    pub weight: u16,
    pub style: String,
}

/// List installed system fonts with metadata and file paths
#[tauri::command]
pub async fn cpres_list_system_fonts() -> Result<Vec<SystemFontInfo>, String> {
    let source = SystemSource::new();
    let handles = source
        .all_fonts()
        .map_err(|e| format!("Failed to list fonts: {e}"))?;

    let mut fonts = Vec::new();
    for handle in handles {
        let path = match &handle {
            Handle::Path { path, .. } => path,
            _ => continue,
        };

        let font = match handle.load() {
            Ok(font) => font,
            Err(_) => continue,
        };

        let properties = font.properties();
        let style = match properties.style {
            Style::Italic | Style::Oblique => "italic",
            _ => "normal",
        }
        .to_string();

        fonts.push(SystemFontInfo {
            family: font.family_name(),
            full_name: font.full_name(),
            postscript_name: font.postscript_name(),
            path: path.to_string_lossy().to_string(),
            weight: properties.weight.0 as u16,
            style,
        });
    }

    fonts.sort_by(|a, b| {
        a.family
            .cmp(&b.family)
            .then_with(|| a.full_name.cmp(&b.full_name))
    });

    Ok(fonts)
}

const DOCUMENTS_APP_DIR_NAME: &str = "Church Presenter";
const CONTENT_DIR_CONFIG_FILENAME: &str = "content_dir.json";
const MEDIA_LIBRARY_DIR_NAME: &str = "media-library";

#[cfg(target_os = "windows")]
fn get_monitor_friendly_name(device_name: &str) -> Option<String> {
    let device_name_w: Vec<u16> = device_name.encode_utf16().chain(std::iter::once(0)).collect();
    let mut display_device = DISPLAY_DEVICEW::default();
    display_device.cb = std::mem::size_of::<DISPLAY_DEVICEW>() as u32;

    let success = unsafe { EnumDisplayDevicesW(PCWSTR(device_name_w.as_ptr()), 0, &mut display_device, 0) }
        .as_bool();
    if !success {
        return None;
    }

    let len = display_device
        .DeviceString
        .iter()
        .position(|c| *c == 0)
        .unwrap_or(display_device.DeviceString.len());
    let friendly = String::from_utf16_lossy(&display_device.DeviceString[..len]).trim().to_string();
    if friendly.is_empty() {
        None
    } else {
        Some(friendly)
    }
}

#[cfg(target_os = "windows")]
fn get_monitor_refresh_rate(device_name: &str) -> Option<u32> {
    let device_name_w: Vec<u16> = device_name.encode_utf16().chain(std::iter::once(0)).collect();
    let mut devmode = DEVMODEW::default();
    devmode.dmSize = std::mem::size_of::<DEVMODEW>() as u16;

    let success =
        unsafe { EnumDisplaySettingsW(PCWSTR(device_name_w.as_ptr()), ENUM_CURRENT_SETTINGS, &mut devmode) }
            .as_bool();
    if !success {
        return None;
    }

    let freq = devmode.dmDisplayFrequency;
    if freq == 0 {
        None
    } else {
        Some(freq)
    }
}

#[cfg(not(target_os = "windows"))]
fn get_monitor_friendly_name(_device_name: &str) -> Option<String> {
    None
}

#[cfg(not(target_os = "windows"))]
fn get_monitor_refresh_rate(_device_name: &str) -> Option<u32> {
    None
}

#[derive(serde::Serialize, serde::Deserialize)]
struct ContentDirConfig {
    path: String,
}

fn content_dir_config_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_data_dir()
        .map(|dir| dir.join(CONTENT_DIR_CONFIG_FILENAME))
        .map_err(|e| e.to_string())
}

fn read_content_dir_config(app: &tauri::AppHandle) -> Result<Option<PathBuf>, String> {
    let config_path = content_dir_config_path(app)?;
    if !config_path.exists() {
        return Ok(None);
    }

    let content = std::fs::read_to_string(&config_path).map_err(|e| e.to_string())?;
    let parsed: ContentDirConfig = serde_json::from_str(&content).map_err(|e| e.to_string())?;
    if parsed.path.trim().is_empty() {
        return Ok(None);
    }

    Ok(Some(PathBuf::from(parsed.path)))
}

fn write_content_dir_config(app: &tauri::AppHandle, path: &Path) -> Result<(), String> {
    let config_path = content_dir_config_path(app)?;
    if let Some(parent) = config_path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }

    let config = ContentDirConfig {
        path: path.to_string_lossy().to_string(),
    };
    let content = serde_json::to_string_pretty(&config).map_err(|e| e.to_string())?;
    std::fs::write(&config_path, content).map_err(|e| e.to_string())?;
    Ok(())
}

fn resolve_content_dir(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    if let Some(configured) = read_content_dir_config(app)? {
        return Ok(configured);
    }

    app.path()
        .resolve(DOCUMENTS_APP_DIR_NAME, BaseDirectory::Document)
        .map_err(|e| e.to_string())
}

fn move_file_with_fallback(source: &Path, destination: &Path) -> Result<(), String> {
    if let Some(parent) = destination.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }

  match std::fs::rename(source, destination) {
    Ok(()) => Ok(()),
    Err(_error) => {
      std::fs::copy(source, destination).map_err(|e| e.to_string())?;
      std::fs::remove_file(source).map_err(|e| e.to_string())?;
      Ok(())
    }
  }
}

fn move_dir_contents(source: &Path, destination: &Path) -> Result<(), String> {
    if !source.exists() {
        return Ok(());
    }

    std::fs::create_dir_all(destination).map_err(|e| e.to_string())?;
    for entry in std::fs::read_dir(source).map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        let entry_path = entry.path();
        let target_path = destination.join(entry.file_name());

        if entry.file_type().map_err(|e| e.to_string())?.is_dir() {
            move_dir_contents(&entry_path, &target_path)?;
            if entry_path.read_dir().map_err(|e| e.to_string())?.next().is_none() {
                let _ = std::fs::remove_dir(&entry_path);
            }
        } else {
            if target_path.exists() {
                std::fs::remove_file(&target_path).map_err(|e| e.to_string())?;
            }
            move_file_with_fallback(&entry_path, &target_path)?;
        }
    }

    Ok(())
}

/// Get the app data directory path
#[tauri::command]
pub fn get_app_data_dir(app: tauri::AppHandle) -> Result<String, String> {
    app.path()
        .app_data_dir()
        .map(|p| p.to_string_lossy().to_string())
        .map_err(|e| e.to_string())
}

/// Get the documents app data directory path
#[tauri::command]
pub fn get_documents_data_dir(app: tauri::AppHandle) -> Result<String, String> {
    resolve_content_dir(&app).map(|p| p.to_string_lossy().to_string())
}

/// Set the content directory (optionally moving existing data)
#[tauri::command]
pub async fn set_content_dir(
    app: tauri::AppHandle,
    path: String,
    move_existing: bool,
    media_library_dir: Option<String>,
) -> Result<String, String> {
    let new_dir = PathBuf::from(path.trim());
    if new_dir.as_os_str().is_empty() {
        return Err("Content folder path is required".to_string());
    }
    if !new_dir.is_absolute() {
        return Err("Content folder path must be absolute".to_string());
    }

    let current_dir = resolve_content_dir(&app)?;
    if new_dir == current_dir {
        write_content_dir_config(&app, &new_dir)?;
        return Ok(new_dir.to_string_lossy().to_string());
    }

    if new_dir.starts_with(&current_dir) {
        return Err("Content folder cannot be inside the current folder".to_string());
    }

    std::fs::create_dir_all(&new_dir).map_err(|e| e.to_string())?;

    if move_existing {
        move_dir_contents(&current_dir, &new_dir)?;
    }

    if let Some(media_dir) = media_library_dir {
        let media_source = PathBuf::from(media_dir);
        let media_target = new_dir.join(MEDIA_LIBRARY_DIR_NAME);
        if media_source.exists() && media_source != media_target {
            move_dir_contents(&media_source, &media_target)?;
            if media_source.read_dir().map_err(|e| e.to_string())?.next().is_none() {
                let _ = std::fs::remove_dir(&media_source);
            }
        }
    }

    write_content_dir_config(&app, &new_dir)?;

    Ok(new_dir.to_string_lossy().to_string())
}

/// Ensure the app data directory exists
#[tauri::command]
pub async fn ensure_app_data_dir(app: tauri::AppHandle) -> Result<String, String> {
    let dir = app.path().app_data_dir().map_err(|e| e.to_string())?;

    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    Ok(dir.to_string_lossy().to_string())
}

/// Ensure the documents app data directory exists
#[tauri::command]
pub async fn ensure_documents_data_dir(app: tauri::AppHandle) -> Result<String, String> {
    let dir = resolve_content_dir(&app)?;

    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    Ok(dir.to_string_lossy().to_string())
}

/// Ensure a subdirectory exists within app data
#[tauri::command]
pub async fn ensure_app_data_subdir(
    app: tauri::AppHandle,
    sub_dir: String,
) -> Result<String, String> {
    let base_dir = app.path().app_data_dir().map_err(|e| e.to_string())?;

    let dir = base_dir.join(&sub_dir);
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    Ok(dir.to_string_lossy().to_string())
}

/// Ensure a subdirectory exists within documents app data
#[tauri::command]
pub async fn ensure_documents_data_subdir(
    app: tauri::AppHandle,
    sub_dir: String,
) -> Result<String, String> {
    let base_dir = resolve_content_dir(&app)?;

    let dir = base_dir.join(&sub_dir);
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    Ok(dir.to_string_lossy().to_string())
}

/// Read a JSON file from app data directory
#[tauri::command]
pub async fn read_app_data_file(app: tauri::AppHandle, filename: String) -> Result<String, String> {
    let dir = app.path().app_data_dir().map_err(|e| e.to_string())?;

    let path = dir.join(&filename);

    if !path.exists() {
        return Err("File not found".to_string());
    }

    std::fs::read_to_string(&path).map_err(|e| e.to_string())
}

/// Read a JSON file from documents app data directory
#[tauri::command]
pub async fn read_documents_data_file(
    app: tauri::AppHandle,
    filename: String,
) -> Result<String, String> {
    let dir = resolve_content_dir(&app)?;

    let path = dir.join(&filename);

    if !path.exists() {
        return Err("File not found".to_string());
    }

    std::fs::read_to_string(&path).map_err(|e| e.to_string())
}

/// Write a JSON file to app data directory
#[tauri::command]
pub async fn write_app_data_file(
    app: tauri::AppHandle,
    filename: String,
    content: String,
) -> Result<(), String> {
    let dir = app.path().app_data_dir().map_err(|e| e.to_string())?;

    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let path = dir.join(&filename);
    std::fs::write(&path, content).map_err(|e| e.to_string())
}

/// Write a JSON file to documents app data directory
#[tauri::command]
pub async fn write_documents_data_file(
    app: tauri::AppHandle,
    filename: String,
    content: String,
) -> Result<(), String> {
    let dir = resolve_content_dir(&app)?;

    let path = dir.join(&filename);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }

    std::fs::write(&path, content).map_err(|e| e.to_string())
}

/// Allow a media library directory in the fs scope (persisted)
#[tauri::command]
pub async fn allow_media_library_dir(
    app: tauri::AppHandle,
    path: String,
) -> Result<(), String> {
    let dir = PathBuf::from(path);
    if !dir.exists() {
        return Err("Directory not found".to_string());
    }
    if !dir.is_dir() {
        return Err("Path is not a directory".to_string());
    }

    let scope = app.fs_scope();
    scope
        .allow_directory(&dir, true)
        .map_err(|e| e.to_string())?;

    Ok(())
}
fn output_window_label(monitor_index: usize) -> String {
    format!("output-{}", monitor_index)
}

fn position_output_window(
    window: &tauri::WebviewWindow,
    monitor_index: usize,
) -> Result<(), String> {
    if let Some(monitor) = window
        .available_monitors()
        .map_err(|e| e.to_string())?
        .get(monitor_index)
    {
        let pos = monitor.position();
        window
            .set_position(tauri::Position::Physical(tauri::PhysicalPosition {
                x: pos.x,
                y: pos.y,
            }))
            .map_err(|e| e.to_string())?;
        window.set_fullscreen(true).map_err(|e| e.to_string())?;
    }

    Ok(())
}

/// Open output windows on the specified monitors
#[tauri::command]
pub async fn open_output_windows(
    app: tauri::AppHandle,
    monitor_indices: Vec<usize>,
) -> Result<(), String> {
    let mut desired_labels = std::collections::HashSet::new();

    for idx in &monitor_indices {
        desired_labels.insert(output_window_label(*idx));
    }

    // Close any output windows not in the desired set (including legacy "output" window)
    for (label, window) in app.webview_windows() {
        if label == "output" || label.starts_with("output-") {
            if !desired_labels.contains(&label) {
                let _ = window.close();
            }
        }
    }

    // Create or reposition desired output windows
    for idx in monitor_indices {
        let label = output_window_label(idx);
        if let Some(window) = app.get_webview_window(&label) {
            window.show().map_err(|e| e.to_string())?;
            position_output_window(&window, idx)?;
            continue;
        }

        let builder = tauri::WebviewWindowBuilder::new(
            &app,
            label,
            tauri::WebviewUrl::App("/output".into()),
        )
        .title("Presentation Output")
        .decorations(false)
        .always_on_top(true);

        let window = builder.build().map_err(|e| e.to_string())?;
        position_output_window(&window, idx)?;
    }

    Ok(())
}

/// Close all output windows
#[tauri::command]
pub async fn close_output_windows(app: tauri::AppHandle) -> Result<(), String> {
    for (label, window) in app.webview_windows() {
        if label == "output" || label.starts_with("output-") {
            let _ = window.close();
        }
    }
    Ok(())
}

/// Get list of available monitors
#[tauri::command]
pub async fn get_monitors(app: tauri::AppHandle) -> Result<Vec<MonitorInfo>, String> {
    let window = app
        .get_webview_window("main")
        .ok_or("Main window not found")?;

    let monitors = window.available_monitors().map_err(|e| e.to_string())?;
    let primary_monitor = window.primary_monitor().map_err(|e| e.to_string())?;

    let mut info = Vec::new();
    for (i, monitor) in monitors.iter().enumerate() {
        let is_primary = primary_monitor.as_ref().is_some_and(|primary| {
            primary.position() == monitor.position()
                && primary.size() == monitor.size()
                && primary.name() == monitor.name()
        });

        let raw_name = monitor.name().cloned();
        let name = raw_name
            .as_deref()
            .and_then(get_monitor_friendly_name)
            .or_else(|| raw_name.clone())
            .unwrap_or_else(|| format!("Monitor {}", i + 1));
        let refresh_rate = raw_name
            .as_deref()
            .and_then(get_monitor_refresh_rate);

        info.push(MonitorInfo {
            index: i,
            name,
            width: monitor.size().width,
            height: monitor.size().height,
            x: monitor.position().x,
            y: monitor.position().y,
            is_primary,
            refresh_rate,
        });
    }

    Ok(info)
}

#[derive(serde::Serialize)]
pub struct MonitorInfo {
    pub index: usize,
    pub name: String,
    pub width: u32,
    pub height: u32,
    pub x: i32,
    pub y: i32,
    pub is_primary: bool,
    pub refresh_rate: Option<u32>,
}
