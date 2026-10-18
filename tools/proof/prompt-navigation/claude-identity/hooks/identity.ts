export type PromptEvidence = {
    messageId: string;
    stored: boolean;
    renderRequested: boolean;
};

// Metadata only. Insertion order is observation order, NOT conversation order.
// A render request does not establish a terminal row or a successful paint.
export class PromptIdentityLedger {
    epoch = 0;
    active = false;
    private sessionId: string | null = null;
    private entries = new Map<string, PromptEvidence>();
    private overflow = false;
    private capacity: number;

    constructor(capacity = 4096) {
        if (!Number.isInteger(capacity) || capacity < 1 || capacity > 4096) throw new Error('Invalid capacity');
        this.capacity = capacity;
    }

    reset(active: boolean) {
        this.epoch++;
        this.active = active;
        this.sessionId = null;
        this.entries.clear();
        this.overflow = false;
    }

    bind(sessionId: string, epoch: number): boolean {
        if (!this.active || epoch !== this.epoch) return false;
        if (!validId(sessionId) || (this.sessionId !== null && this.sessionId !== sessionId)) {
            this.reset(false); // An unannounced session switch is not a renewal.
            return false;
        }
        this.sessionId = sessionId;
        return true;
    }

    record(messageId: string, source: 'stored' | 'renderRequested', epoch: number) {
        if (!this.active || this.sessionId === null || epoch !== this.epoch || !validId(messageId)) return;
        const previous = this.entries.get(messageId);
        if (!previous && this.entries.size === this.capacity) {
            this.overflow = true;
            return;
        }
        this.entries.set(messageId, { messageId, stored: false, renderRequested: false, ...previous, [source]: true });
    }

    report() {
        return {
            agent: 'claude-code', sessionId: this.sessionId, epoch: this.epoch,
            active: this.active, coverage: 'observed-only', overflow: this.overflow,
            navigationEligible: false,
            reason: 'No qualified terminal anchor or key bridge',
            prompts: [...this.entries.values()].map(value => ({ ...value })),
        };
    }
}

function validId(id: string): boolean {
    return typeof id === 'string' && id.trim().length > 0 && id.length <= 512;
}
