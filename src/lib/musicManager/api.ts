/**
 * Music Manager API - typed functions for interacting with the NCBF Music Manager
 * Supabase tables (songs, sets, groups, etc.)
 */

import { supabase } from '../supabase';
import type {
  MusicGroup,
  Song,
  SongArrangement,
  SongAsset,
  Set,
  SetSong,
  SongWithAssets,
  SetWithSongs,
  PresenterLink,
  SyncRequest,
} from '../supabase';

// ============================================================================
// Music Groups
// ============================================================================

export async function fetchMusicGroups(): Promise<MusicGroup[]> {
  const { data, error } = await supabase
    .from('music_groups')
    .select('*')
    .order('name');

  if (error) throw new Error(`Failed to fetch music groups: ${error.message}`);
  return data ?? [];
}

export async function fetchMusicGroup(id: string): Promise<MusicGroup | null> {
  const { data, error } = await supabase
    .from('music_groups')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch music group: ${error.message}`);
  }
  return data;
}

// ============================================================================
// Songs
// ============================================================================

export async function fetchSongs(groupId?: string): Promise<Song[]> {
  let query = supabase.from('songs').select('*').order('title');

  if (groupId) {
    query = query.eq('group_id', groupId);
  }

  const { data, error } = await query;

  if (error) throw new Error(`Failed to fetch songs: ${error.message}`);
  return data ?? [];
}

export async function fetchSong(id: string): Promise<Song | null> {
  const { data, error } = await supabase
    .from('songs')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch song: ${error.message}`);
  }
  return data;
}

export async function fetchSongWithAssets(id: string): Promise<SongWithAssets | null> {
  const { data, error } = await supabase
    .from('songs')
    .select(`
      *,
      song_assets (*),
      song_arrangements (*)
    `)
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch song with assets: ${error.message}`);
  }
  return data as SongWithAssets | null;
}

export async function searchSongs(query: string, groupId?: string): Promise<Song[]> {
  let dbQuery = supabase
    .from('songs')
    .select('*')
    .ilike('title', `%${query}%`)
    .order('title')
    .limit(50);

  if (groupId) {
    dbQuery = dbQuery.eq('group_id', groupId);
  }

  const { data, error } = await dbQuery;

  if (error) throw new Error(`Failed to search songs: ${error.message}`);
  return data ?? [];
}

// ============================================================================
// Song Assets (for lyrics extraction)
// ============================================================================

export async function fetchSongAssets(songId: string): Promise<SongAsset[]> {
  const { data, error } = await supabase
    .from('song_assets')
    .select('*')
    .eq('song_id', songId)
    .order('created_at', { ascending: false });

  if (error) throw new Error(`Failed to fetch song assets: ${error.message}`);
  return data ?? [];
}

export type ArrangementSlide = {
  label?: string | null;
  lines?: string[] | null;
};

function normalizeArrangementSlides(slides: unknown): ArrangementSlide[] | null {
  if (!Array.isArray(slides)) return null;

  const normalized: ArrangementSlide[] = [];
  for (const slide of slides) {
    if (!slide || typeof slide !== 'object') continue;
    const raw = slide as { label?: unknown; lines?: unknown };
    const label = typeof raw.label === 'string' ? raw.label : null;
    const lines = Array.isArray(raw.lines)
      ? raw.lines.filter((line) => typeof line === 'string') as string[]
      : [];
    normalized.push({ label, lines });
  }

  return normalized.length > 0 ? normalized : null;
}

export async function fetchSongArrangementSlides(songId: string): Promise<ArrangementSlide[] | null> {
  const { data, error } = await supabase
    .from('song_arrangements')
    .select('id, name, slides, created_at')
    .eq('song_id', songId)
    .order('created_at', { ascending: true });

  if (error) throw new Error(`Failed to fetch song arrangements: ${error.message}`);
  const arrangements = data ?? [];
  const preferred =
    arrangements.find((arr) => arr.slides && arr.name?.toLowerCase() === 'default') ??
    arrangements.find((arr) => arr.slides);

  return preferred ? normalizeArrangementSlides(preferred.slides) : null;
}

// ============================================================================
// Song Arrangements
// ============================================================================

export async function fetchSongArrangements(songId: string): Promise<SongArrangement[]> {
  const { data, error } = await supabase
    .from('song_arrangements')
    .select('*')
    .eq('song_id', songId)
    .order('name');

  if (error) throw new Error(`Failed to fetch arrangements: ${error.message}`);
  return data ?? [];
}

// ============================================================================
// Sets (Set Lists)
// ============================================================================

export async function fetchSets(groupId: string): Promise<Set[]> {
  const { data, error } = await supabase
    .from('sets')
    .select('*')
    .eq('group_id', groupId)
    .order('service_date', { ascending: false });

  if (error) throw new Error(`Failed to fetch sets: ${error.message}`);
  return data ?? [];
}

export async function fetchSet(id: string): Promise<Set | null> {
  const { data, error } = await supabase
    .from('sets')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch set: ${error.message}`);
  }
  return data;
}

export async function fetchSetWithSongs(id: string): Promise<SetWithSongs | null> {
  const { data, error } = await supabase
    .from('sets')
    .select(`
      *,
      set_songs (
        *,
        songs (*)
      )
    `)
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch set with songs: ${error.message}`);
  }

  // Sort set_songs by position
  if (data) {
    const typedData = data as SetWithSongs;
    if (typedData.set_songs) {
      typedData.set_songs.sort((a, b) => a.position - b.position);
    }
    return typedData;
  }

  return null;
}

export async function fetchSetSongs(setId: string): Promise<(SetSong & { songs?: Song })[]> {
  const { data, error } = await supabase
    .from('set_songs')
    .select(`
      *,
      songs (*)
    `)
    .eq('set_id', setId)
    .order('position');

  if (error) throw new Error(`Failed to fetch set songs: ${error.message}`);
  return (data ?? []) as (SetSong & { songs?: Song })[];
}

// ============================================================================
// Presenter Links (sync status)
// ============================================================================

export async function fetchPresenterLink(localPresentationId: string): Promise<PresenterLink | null> {
  const { data, error } = await supabase
    .from('presenter_links')
    .select('*')
    .eq('local_presentation_id', localPresentationId)
    .maybeSingle();

  if (error) throw new Error(`Failed to fetch presenter link: ${error.message}`);
  return data;
}

export async function upsertPresenterLink(
  link: Omit<PresenterLink, 'id' | 'updated_at'>
): Promise<PresenterLink> {
  const insertData = {
    ...link,
    updated_at: new Date().toISOString(),
  };

  const { data, error } = await supabase
    .from('presenter_links')
    .upsert(insertData, {
      onConflict: 'local_presentation_id',
    })
    .select()
    .single();

  if (error) throw new Error(`Failed to upsert presenter link: ${error.message}`);
  return data;
}

// ============================================================================
// Sync Requests
// ============================================================================

export async function createSyncRequest(
  request: Omit<SyncRequest, 'id' | 'status' | 'conflict_url' | 'created_at' | 'updated_at'>
): Promise<SyncRequest> {
  const insertData = {
    ...request,
    status: 'queued' as const,
  };

  const { data, error } = await supabase
    .from('sync_requests')
    .insert(insertData)
    .select()
    .single();

  if (error) throw new Error(`Failed to create sync request: ${error.message}`);
  return data;
}

export async function fetchSyncRequest(id: string): Promise<SyncRequest | null> {
  const { data, error } = await supabase
    .from('sync_requests')
    .select('*')
    .eq('id', id)
    .single();

  if (error && error.code !== 'PGRST116') {
    throw new Error(`Failed to fetch sync request: ${error.message}`);
  }
  return data;
}

export async function fetchPendingSyncRequests(groupId: string): Promise<SyncRequest[]> {
  const { data, error } = await supabase
    .from('sync_requests')
    .select('*')
    .eq('group_id', groupId)
    .in('status', ['queued', 'conflict'])
    .order('created_at', { ascending: false });

  if (error) throw new Error(`Failed to fetch pending sync requests: ${error.message}`);
  return data ?? [];
}

// ============================================================================
// Convenience: Create set update sync request (for playlist reorder)
// ============================================================================

export async function requestSetUpdate(
  groupId: string,
  setId: string,
  newOrder: { songId: string; position: number }[]
): Promise<SyncRequest> {
  return createSyncRequest({
    group_id: groupId,
    type: 'set_update',
    target_id: setId,
    base_version: null, // Could track version if we implement optimistic locking
    payload: {
      order: newOrder,
    },
  });
}
