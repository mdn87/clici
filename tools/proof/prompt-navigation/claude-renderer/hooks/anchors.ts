type Entry = { messageId: string; nonce: string; stored: boolean; rendered: boolean; order?: number };

// The renderer emits only colored spaces. Native IDs stay in memory and in the
// explicit diagnostic report; user text never enters the registry.
export class PromptAnchors {
    epoch = 0;
    sessionId: string | null = null;
    active = false;
    overflow = false;
    private entries = new Map<string, Entry>();
    private sequence = 0;
    private readonly nonce: () => string;
    private readonly capacity: number;

    constructor(nonce: () => string, capacity = 4096) {
        if (!Number.isInteger(capacity) || capacity < 1 || capacity > 4096) throw new Error('Invalid capacity');
        this.nonce = nonce;
        this.capacity = capacity;
    }

    reset(active: boolean) {
        this.epoch++;
        this.active = active;
        this.sessionId = null;
        this.overflow = false;
        this.entries.clear();
        this.sequence = 0;
    }

    bind(id: string, epoch: number): boolean {
        if (!this.active || epoch !== this.epoch) return false;
        if (!validId(id) || (this.sessionId !== null && this.sessionId !== id)) {
            this.reset(false);
            return false;
        }
        this.sessionId = id;
        return true;
    }

    private entry(id: string, epoch: number): Entry | undefined {
        if (!this.active || this.overflow || this.sessionId === null || epoch !== this.epoch || !validId(id)) return;
        let entry = this.entries.get(id);
        if (!entry) {
            if (this.entries.size >= this.capacity) { this.overflow = true; return; }
            // The helper map accepts only uppercase hex, the form Codex emits.
            const nonce = this.nonce().toUpperCase();
            if (!/^[a-fA-F0-9]{32}$/.test(nonce)) { this.reset(false); return; }
            entry = { messageId: id, nonce, stored: false, rendered: false };
            this.entries.set(id, entry);
        }
        return entry;
    }

    reserveOrder(): number { return this.sequence++; }

    beginAppend(id: string, epoch: number, order = this.reserveOrder()) {
        const entry = this.entry(id, epoch);
        if (entry && entry.order === undefined) entry.order = order;
    }

    commit(id: string, epoch: number) {
        const entry = this.entry(id, epoch);
        if (entry && entry.order !== undefined) entry.stored = true;
    }

    render(id: string, epoch: number): string | undefined {
        const entry = this.entry(id, epoch);
        if (entry) entry.rendered = true;
        return entry?.nonce;
    }

    report() {
        return {
            agent: 'claude-code', sessionId: this.sessionId, epoch: this.epoch,
            active: this.active, overflow: this.overflow, historyComplete: false,
            renderRequests: [...this.entries.values()].filter(e => e.rendered).map(e => e.messageId),
            navigationEligible: false,
            reason: 'Renderer anchors require physical verification; keyboard scope is not established',
            // Append invocation order is provisional. The COM resolver must verify
            // that complete pairs occur in this same order in the actual buffer.
            // Include committed prompts before they paint, so a missing newest
            // anchor refuses navigation rather than silently omitting a turn.
            prompts: [...this.entries.values()].filter(e => e.stored)
                .sort((a, b) => a.order! - b.order!)
                .map(({ messageId, nonce }) => ({ messageId, nonce })),
        };
    }
}

export function anchorColors(nonce: string, end: boolean): string[] {
    if (!/^[a-fA-F0-9]{32}$/.test(nonce)) throw new Error('Expected a 128-bit anchor nonce');
    return (nonce + (end ? 'c3d4' : 'a1b2')).match(/.{6}/g)!.map(rgb => '#' + rgb.toLowerCase());
}

function validId(id: string): boolean {
    return typeof id === 'string' && id.trim().length > 0 && id.length <= 512;
}
