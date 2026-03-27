export type Json =
  | string
  | number
  | boolean
  | null
  | { [key: string]: Json | undefined }
  | Json[]

export type Database = {
  __InternalSupabase: {
    PostgrestVersion: '14.1'
  }
  public: {
    Tables: {
      music_groups: {
        Row: {
          created_at: string
          id: string
          name: string
          slug: string
        }
        Insert: {
          created_at?: string
          id?: string
          name: string
          slug: string
        }
        Update: {
          created_at?: string
          id?: string
          name?: string
          slug?: string
        }
        Relationships: []
      }
      presenter_links: {
        Row: {
          conflict_url: string | null
          group_id: string
          id: string
          local_presentation_id: string
          local_updated_at: string
          remote_presentation_id: string | null
          remote_version: number | null
          song_id: string | null
          status: string
          updated_at: string
        }
        Insert: {
          conflict_url?: string | null
          group_id: string
          id?: string
          local_presentation_id: string
          local_updated_at?: string
          remote_presentation_id?: string | null
          remote_version?: number | null
          song_id?: string | null
          status?: string
          updated_at?: string
        }
        Update: {
          conflict_url?: string | null
          group_id?: string
          id?: string
          local_presentation_id?: string
          local_updated_at?: string
          remote_presentation_id?: string | null
          remote_version?: number | null
          song_id?: string | null
          status?: string
          updated_at?: string
        }
        Relationships: [
          {
            foreignKeyName: 'presenter_links_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'presenter_links_song_id_fkey'
            columns: ['song_id']
            isOneToOne: false
            referencedRelation: 'songs'
            referencedColumns: ['id']
          }
        ]
      }
      set_songs: {
        Row: {
          arrangement_id: string | null
          id: string
          key_override: string | null
          notes: string | null
          position: number
          set_id: string
          song_id: string
        }
        Insert: {
          arrangement_id?: string | null
          id?: string
          key_override?: string | null
          notes?: string | null
          position: number
          set_id: string
          song_id: string
        }
        Update: {
          arrangement_id?: string | null
          id?: string
          key_override?: string | null
          notes?: string | null
          position?: number
          set_id?: string
          song_id?: string
        }
        Relationships: [
          {
            foreignKeyName: 'set_songs_arrangement_id_fkey'
            columns: ['arrangement_id']
            isOneToOne: false
            referencedRelation: 'song_arrangements'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'set_songs_set_id_fkey'
            columns: ['set_id']
            isOneToOne: false
            referencedRelation: 'sets'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'set_songs_song_id_fkey'
            columns: ['song_id']
            isOneToOne: false
            referencedRelation: 'songs'
            referencedColumns: ['id']
          }
        ]
      }
      sets: {
        Row: {
          created_at: string
          group_id: string
          id: string
          notes: string | null
          service_date: string
        }
        Insert: {
          created_at?: string
          group_id: string
          id?: string
          notes?: string | null
          service_date: string
        }
        Update: {
          created_at?: string
          group_id?: string
          id?: string
          notes?: string | null
          service_date?: string
        }
        Relationships: [
          {
            foreignKeyName: 'sets_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          }
        ]
      }
      song_arrangements: {
        Row: {
          chords_text: string | null
          created_at: string
          group_id: string | null
          id: string
          is_locked: boolean
          name: string
          notes: string | null
          song_id: string
          updated_at: string
        }
        Insert: {
          chords_text?: string | null
          created_at?: string
          group_id?: string | null
          id?: string
          is_locked?: boolean
          name: string
          notes?: string | null
          song_id: string
          updated_at?: string
        }
        Update: {
          chords_text?: string | null
          created_at?: string
          group_id?: string | null
          id?: string
          is_locked?: boolean
          name?: string
          notes?: string | null
          song_id?: string
          updated_at?: string
        }
        Relationships: [
          {
            foreignKeyName: 'song_arrangements_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'song_arrangements_song_id_fkey'
            columns: ['song_id']
            isOneToOne: false
            referencedRelation: 'songs'
            referencedColumns: ['id']
          }
        ]
      }
      song_assets: {
        Row: {
          arrangement_id: string | null
          asset_type: string
          created_at: string
          extract_status: string
          extract_warning: string | null
          group_id: string | null
          id: string
          mime_type: string
          original_filename: string
          song_id: string
          storage_bucket: string
          storage_path: string
        }
        Insert: {
          arrangement_id?: string | null
          asset_type: string
          created_at?: string
          extract_status?: string
          extract_warning?: string | null
          group_id?: string | null
          id?: string
          mime_type: string
          original_filename: string
          song_id: string
          storage_bucket?: string
          storage_path: string
        }
        Update: {
          arrangement_id?: string | null
          asset_type?: string
          created_at?: string
          extract_status?: string
          extract_warning?: string | null
          group_id?: string | null
          id?: string
          mime_type?: string
          original_filename?: string
          song_id?: string
          storage_bucket?: string
          storage_path?: string
        }
        Relationships: [
          {
            foreignKeyName: 'song_assets_arrangement_id_fkey'
            columns: ['arrangement_id']
            isOneToOne: false
            referencedRelation: 'song_arrangements'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'song_assets_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'song_assets_song_id_fkey'
            columns: ['song_id']
            isOneToOne: false
            referencedRelation: 'songs'
            referencedColumns: ['id']
          }
        ]
      }
      song_arrangement_groups: {
        Row: {
          arrangement_id: string
          id: string
          position: number
          slide_group_id: string
        }
        Insert: {
          arrangement_id: string
          id?: string
          position?: number
          slide_group_id: string
        }
        Update: {
          arrangement_id?: string
          id?: string
          position?: number
          slide_group_id?: string
        }
        Relationships: [
          {
            foreignKeyName: 'song_arrangement_groups_arrangement_id_fkey'
            columns: ['arrangement_id']
            isOneToOne: false
            referencedRelation: 'song_arrangements'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'song_arrangement_groups_slide_group_id_fkey'
            columns: ['slide_group_id']
            isOneToOne: false
            referencedRelation: 'song_slide_groups'
            referencedColumns: ['id']
          }
        ]
      }
      song_revisions: {
        Row: {
          artist: string | null
          ccli_id: string | null
          created_at: string
          default_key: string | null
          group_id: string
          id: string
          link_url: string | null
          slide_groups: Json
          slides: Json
          song_id: string
          title: string
        }
        Insert: {
          artist?: string | null
          ccli_id?: string | null
          created_at?: string
          default_key?: string | null
          group_id: string
          id?: string
          link_url?: string | null
          slide_groups?: Json
          slides?: Json
          song_id: string
          title: string
        }
        Update: {
          artist?: string | null
          ccli_id?: string | null
          created_at?: string
          default_key?: string | null
          group_id?: string
          id?: string
          link_url?: string | null
          slide_groups?: Json
          slides?: Json
          song_id?: string
          title?: string
        }
        Relationships: [
          {
            foreignKeyName: 'song_revisions_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'song_revisions_song_id_fkey'
            columns: ['song_id']
            isOneToOne: false
            referencedRelation: 'songs'
            referencedColumns: ['id']
          }
        ]
      }
      song_slide_groups: {
        Row: {
          created_at: string
          custom_label: string | null
          group_id: string
          id: string
          label: string
          position: number
          slides: Json
          song_id: string
          updated_at: string
        }
        Insert: {
          created_at?: string
          custom_label?: string | null
          group_id: string
          id?: string
          label: string
          position?: number
          slides?: Json
          song_id: string
          updated_at?: string
        }
        Update: {
          created_at?: string
          custom_label?: string | null
          group_id?: string
          id?: string
          label?: string
          position?: number
          slides?: Json
          song_id?: string
          updated_at?: string
        }
        Relationships: [
          {
            foreignKeyName: 'song_slide_groups_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          },
          {
            foreignKeyName: 'song_slide_groups_song_id_fkey'
            columns: ['song_id']
            isOneToOne: false
            referencedRelation: 'songs'
            referencedColumns: ['id']
          }
        ]
      }
      songs: {
        Row: {
          artist: string | null
          ccli_id: string | null
          chord_chart_data: string | null
          created_at: string
          default_key: string | null
          group_id: string
          id: string
          link_url: string | null
          title: string
        }
        Insert: {
          artist?: string | null
          ccli_id?: string | null
          chord_chart_data?: string | null
          created_at?: string
          default_key?: string | null
          group_id: string
          id?: string
          link_url?: string | null
          title: string
        }
        Update: {
          artist?: string | null
          ccli_id?: string | null
          chord_chart_data?: string | null
          created_at?: string
          default_key?: string | null
          group_id?: string
          id?: string
          link_url?: string | null
          title?: string
        }
        Relationships: [
          {
            foreignKeyName: 'songs_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          }
        ]
      }
      sync_requests: {
        Row: {
          base_version: number | null
          conflict_url: string | null
          created_at: string
          group_id: string
          id: string
          payload: Json
          status: string
          target_id: string
          type: string
          updated_at: string
        }
        Insert: {
          base_version?: number | null
          conflict_url?: string | null
          created_at?: string
          group_id: string
          id?: string
          payload?: Json
          status?: string
          target_id: string
          type: string
          updated_at?: string
        }
        Update: {
          base_version?: number | null
          conflict_url?: string | null
          created_at?: string
          group_id?: string
          id?: string
          payload?: Json
          status?: string
          target_id?: string
          type?: string
          updated_at?: string
        }
        Relationships: [
          {
            foreignKeyName: 'sync_requests_group_id_fkey'
            columns: ['group_id']
            isOneToOne: false
            referencedRelation: 'music_groups'
            referencedColumns: ['id']
          }
        ]
      }
    }
    Views: {
      [_ in never]: never
    }
    Functions: {
      [_ in never]: never
    }
    Enums: {
      [_ in never]: never
    }
    CompositeTypes: {
      [_ in never]: never
    }
  }
}

type DatabaseWithoutInternals = Omit<Database, '__InternalSupabase'> & {
  music: Omit<Database, '__InternalSupabase'>['public']
}

type DefaultSchema = DatabaseWithoutInternals[Extract<keyof Database, 'public'>]

export type Tables<
  DefaultSchemaTableNameOrOptions extends
    | keyof (DefaultSchema['Tables'] & DefaultSchema['Views'])
    | { schema: keyof DatabaseWithoutInternals },
  TableName extends DefaultSchemaTableNameOrOptions extends {
    schema: keyof DatabaseWithoutInternals
  }
    ? keyof (DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Tables'] &
        DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Views'])
    : never = never
> = DefaultSchemaTableNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
  ? (DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Tables'] &
      DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Views'])[TableName] extends {
      Row: infer R
    }
    ? R
    : never
  : DefaultSchemaTableNameOrOptions extends keyof (DefaultSchema['Tables'] & DefaultSchema['Views'])
    ? (DefaultSchema['Tables'] & DefaultSchema['Views'])[DefaultSchemaTableNameOrOptions] extends {
        Row: infer R
      }
      ? R
      : never
    : never

export type TablesInsert<
  DefaultSchemaTableNameOrOptions extends
    | keyof DefaultSchema['Tables']
    | { schema: keyof DatabaseWithoutInternals },
  TableName extends DefaultSchemaTableNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
    ? keyof DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Tables']
    : never = never
> = DefaultSchemaTableNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
  ? DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Tables'][TableName] extends {
      Insert: infer I
    }
    ? I
    : never
  : DefaultSchemaTableNameOrOptions extends keyof DefaultSchema['Tables']
    ? DefaultSchema['Tables'][DefaultSchemaTableNameOrOptions] extends {
        Insert: infer I
      }
      ? I
      : never
    : never

export type TablesUpdate<
  DefaultSchemaTableNameOrOptions extends
    | keyof DefaultSchema['Tables']
    | { schema: keyof DatabaseWithoutInternals },
  TableName extends DefaultSchemaTableNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
    ? keyof DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Tables']
    : never = never
> = DefaultSchemaTableNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
  ? DatabaseWithoutInternals[DefaultSchemaTableNameOrOptions['schema']]['Tables'][TableName] extends {
      Update: infer U
    }
    ? U
    : never
  : DefaultSchemaTableNameOrOptions extends keyof DefaultSchema['Tables']
    ? DefaultSchema['Tables'][DefaultSchemaTableNameOrOptions] extends {
        Update: infer U
      }
      ? U
      : never
    : never

export type Enums<
  DefaultSchemaEnumNameOrOptions extends
    | keyof DefaultSchema['Enums']
    | { schema: keyof DatabaseWithoutInternals },
  EnumName extends DefaultSchemaEnumNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
    ? keyof DatabaseWithoutInternals[DefaultSchemaEnumNameOrOptions['schema']]['Enums']
    : never = never
> = DefaultSchemaEnumNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
  ? DatabaseWithoutInternals[DefaultSchemaEnumNameOrOptions['schema']]['Enums'][EnumName]
  : DefaultSchemaEnumNameOrOptions extends keyof DefaultSchema['Enums']
    ? DefaultSchema['Enums'][DefaultSchemaEnumNameOrOptions]
    : never

export type CompositeTypes<
  PublicCompositeTypeNameOrOptions extends
    | keyof DefaultSchema['CompositeTypes']
    | { schema: keyof DatabaseWithoutInternals },
  CompositeTypeName extends PublicCompositeTypeNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
    ? keyof DatabaseWithoutInternals[PublicCompositeTypeNameOrOptions['schema']]['CompositeTypes']
    : never = never
> = PublicCompositeTypeNameOrOptions extends { schema: keyof DatabaseWithoutInternals }
  ? DatabaseWithoutInternals[PublicCompositeTypeNameOrOptions['schema']]['CompositeTypes'][CompositeTypeName]
  : PublicCompositeTypeNameOrOptions extends keyof DefaultSchema['CompositeTypes']
    ? DefaultSchema['CompositeTypes'][PublicCompositeTypeNameOrOptions]
    : never

export const Constants = {
  public: {
    Enums: {}
  },
  music: {
    Enums: {}
  }
} as const

// Convenience aliases for app code
export type MusicGroup = Tables<{ schema: 'music' }, 'music_groups'>
export type Song = Tables<{ schema: 'music' }, 'songs'>
export type SongArrangement = Tables<{ schema: 'music' }, 'song_arrangements'>
export type SongAsset = Tables<{ schema: 'music' }, 'song_assets'>
export type Set = Tables<{ schema: 'music' }, 'sets'>
export type SetSong = Tables<{ schema: 'music' }, 'set_songs'>
export type PresenterLink = Tables<{ schema: 'music' }, 'presenter_links'>
export type SyncRequest = Tables<{ schema: 'music' }, 'sync_requests'>

// Extended helper types
export interface SongWithAssets extends Song {
  song_assets?: SongAsset[]
  song_arrangements?: SongArrangement[]
}

export interface SetWithSongs extends Set {
  set_songs?: (SetSong & { songs?: Song })[]
}

export type SyncStatus = PresenterLink['status']
export type SyncRequestType = SyncRequest['type']
export type SyncRequestStatus = SyncRequest['status']
