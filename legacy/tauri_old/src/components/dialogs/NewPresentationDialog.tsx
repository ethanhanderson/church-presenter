/**
 * NewPresentationDialog - Dialog for creating a new presentation
 */

import { useEffect, useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { Library, SlideType } from '@/lib/models';

interface NewPresentationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (title: string, type: SlideType, libraryId: string | null) => void;
  libraries: Library[];
  defaultLibraryId?: string | null;
}

export function NewPresentationDialog({
  open,
  onOpenChange,
  onConfirm,
  libraries,
  defaultLibraryId = null,
}: NewPresentationDialogProps) {
  const [title, setTitle] = useState('');
  const [type, setType] = useState<SlideType>('song');
  const [libraryId, setLibraryId] = useState('');

  useEffect(() => {
    if (open) {
      setTitle('');
      setType('song');
      if (defaultLibraryId) {
        setLibraryId(defaultLibraryId);
      } else if (libraries.length === 1) {
        setLibraryId(libraries[0]!.id);
      } else {
        setLibraryId('');
      }
    }
  }, [open, defaultLibraryId, libraries]);

  const requiresLibrary = type === 'song';
  const isLibraryValid = !requiresLibrary || !!libraryId;

  const handleConfirm = () => {
    if (title.trim() && isLibraryValid) {
      onConfirm(title.trim(), type, libraryId || null);
      setTitle('');
      setType('song');
      setLibraryId('');
      onOpenChange(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New Presentation</DialogTitle>
          <DialogDescription>
            Create a new presentation for worship
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-4">
          <div className="space-y-2">
            <Label htmlFor="title">Title</Label>
            <Input
              id="title"
              placeholder="Enter presentation title..."
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleConfirm()}
              autoFocus
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="type">Type</Label>
            <Select value={type} onValueChange={(v) => setType(v as SlideType)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="song">Song / Worship</SelectItem>
                <SelectItem value="sermon">Sermon Notes</SelectItem>
                <SelectItem value="scripture">Scripture</SelectItem>
                <SelectItem value="announcement">Announcement</SelectItem>
                <SelectItem value="blank">Blank</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {requiresLibrary && (
            <div className="space-y-2">
              <Label htmlFor="library">Library</Label>
              {libraries.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  Create a library before adding songs.
                </p>
              ) : (
                <Select value={libraryId} onValueChange={setLibraryId}>
                  <SelectTrigger id="library">
                    <SelectValue placeholder="Select a library" />
                  </SelectTrigger>
                  <SelectContent>
                    {libraries.map((library) => (
                      <SelectItem key={library.id} value={library.id}>
                        {library.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button onClick={handleConfirm} disabled={!title.trim() || !isLibraryValid}>
            Create
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
