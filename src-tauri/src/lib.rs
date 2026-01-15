mod commands;
mod cpres;

use commands::*;
use tauri::{Emitter, Manager};
use tauri_plugin_log::{Target, TargetKind};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, args, cwd| {
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.show();
                let _ = window.set_focus();
            }

            let open_path = args.iter().find_map(|arg| {
                let trimmed = arg.trim_matches('"');
                let path = std::path::Path::new(trimmed);
                let is_cpres = path
                    .extension()
                    .and_then(|ext| ext.to_str())
                    .map(|ext| ext.eq_ignore_ascii_case("cpres"))
                    .unwrap_or(false);

                if !is_cpres {
                    return None;
                }

                let resolved = if path.is_absolute() {
                    path.to_path_buf()
                } else {
                    std::path::Path::new(&cwd).join(path)
                };

                Some(resolved.to_string_lossy().to_string())
            });

            if let Some(path) = open_path {
                let _ = app.emit_to("main", "app:open-path", OpenPathPayload { path });
            }
        }))
        .plugin(
            tauri_plugin_log::Builder::new()
                .level(tauri_plugin_log::log::LevelFilter::Trace)
                .target(Target::new(TargetKind::Stdout))
                .build(),
        )
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_persisted_scope::init())
        .plugin(tauri_plugin_store::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_window_state::Builder::new().build())
        .invoke_handler(tauri::generate_handler![
            cpres_open,
            cpres_save,
            cpres_read_media,
            cpres_import_media,
            get_app_data_dir,
            get_documents_data_dir,
            set_content_dir,
            ensure_app_data_dir,
            ensure_documents_data_dir,
            ensure_app_data_subdir,
            ensure_documents_data_subdir,
            read_app_data_file,
            read_documents_data_file,
            write_app_data_file,
            write_documents_data_file,
            allow_media_library_dir,
            open_output_windows,
            close_output_windows,
            get_monitors,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[derive(Clone, serde::Serialize)]
struct OpenPathPayload {
    path: String,
}
