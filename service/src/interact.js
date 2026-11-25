// O lado do objeto da FC nos cliques do Discord: monta cada tela do /fcc, grava ajustes, rascunhos, inscrições
// e eventos. Entra no FcState com Object.assign (ver index.js). `this` é o objeto da FC.

import { EPHEMERAL, REPLY } from './discord.js';
import {
  applyFields, changeDraft, dueReminders, closeDue, draftReview, eventFromDraft, eventLine, eventModal, eventPost, eventsWakeAt,
  MAX_OPEN, newDraft, nextOccurrence, openEvents, pruneDue, signUp, attendees, typePicker,
} from './events.js';
import {
  dayOf, lootSummary, newState, profitSummary, renderPanel, subsSummary, suppliesSummary, alertDeleteAt,
} from './logic.js';
import {
  MODAL, menuMessage, screenMessage, sectionEmbed, sharedMessage, UPDATE_MESSAGE,
} from './menu.js';
import { langOf, text } from './texts.js';
import { sendDm, transportOf, DISCORD_API } from './transport.js';

const nowSeconds = () => Math.floor(Date.now() / 1000);
const json = (body) => Response.json(body);
const newId = () => crypto.randomUUID().replace(/-/g, '').slice(0, 10);
// O rascunho de evento some depois de 1 dia sem mexer.
const DRAFT_TTL = 86400;

// Título do loot no idioma da FC: "Loot de hoje (25/11)", "Loot of the last 7 days".
function lootTitle(lang, count, now) {
  const [, month, day] = dayOf(now).split('-');
  if (lang === 'en') return count === 1 ? `Loot today (${day}/${month})` : `Loot of the last ${count} days`;
  return count === 1 ? `Loot de hoje (${day}/${month})` : `Loot dos últimos ${count} dias`;
}

const canCreate = (p, settings) => p.manager || Boolean(settings?.eventRole && (p.roles ?? []).includes(settings.eventRole));
const note = (content) => ({ response: { type: REPLY, data: { content, flags: EPHEMERAL, allowed_mentions: { parse: [] } } } });
const update = (data) => ({ response: { type: UPDATE_MESSAGE, data } });

export const InteractMethods = {
  async state() {
    return (await this.ctx.storage.get('state')) ?? newState();
  },

  async events() {
    return (await this.ctx.storage.get('events')) ?? {};
  },

  // Muda um evento relendo a lista antes, porque um envio ao Discord pode ter deixado outra mudança entrar.
  async changeEvent(id, change) {
    const events = await this.events();
    if (!events[id]) return null;
    change(events[id]);
    await this.ctx.storage.put('events', events);
    return events[id];
  },

  async wakeAt(unix) {
    const at = unix * 1000;
    const alarm = await this.ctx.storage.getAlarm();
    if (alarm === null || alarm > at) await this.ctx.storage.setAlarm(Math.max(Date.now(), at));
  },

  async handleInteract(request) {
    const p = await request.json();
    return json(await this.interact(p));
  },

  // O menu do /fcc que está no canal agora. Grava o novo e devolve o que deve ser apagado: o anterior ou, se este
  // chegou atrasado e já existe um aberto depois dele, este mesmo.
  async handleMenu(request) {
    const menu = await request.json();
    const key = `menu:${menu.channel}`;
    const current = { id: menu.id, token: menu.token, at: menu.at, openedAt: menu.openedAt ?? 0 };
    const previous = (await this.ctx.storage.get(key)) ?? null;
    if (previous && (previous.openedAt ?? 0) > current.openedAt) return json(current);
    await this.ctx.storage.put(key, current);
    return json(previous);
  },

  async interact(p) {
    const now = nowSeconds();
    const state = await this.state();
    const lang = langOf(state);
    const t = (key) => text(lang, key);
    const menu = async (type, view, arg, notice, pub = p.pub) => ({ response: { type, data: await this.menuData(view, arg, { ...p, pub }, notice) } });

    switch (p.kind) {
      // O /fcc e os botões do painel abrem o menu no canal.
      case 'command': return menu(REPLY, p.view, p.arg, p.moved ? t('moved') : undefined, true);
      case 'open': return menu(REPLY, p.view, p.arg, undefined, true);
      case 'view':
        // Ajustes, clicado no menu do canal, abre numa mensagem só de quem clicou.
        if (p.pub && p.view === 'manage') return menu(REPLY, 'manage', null, undefined, false);
        return menu(UPDATE_MESSAGE, p.view, p.arg);
      case 'share': {
        const embed = await this.sectionEmbed(p.view, p.arg, state, now, lang, p);
        return { response: { type: REPLY, data: sharedMessage(lang, embed, p.user) } };
      }
      case 'action': return this.action(p, state, lang);
      case 'eventrole': {
        const role = p.values?.[0] ?? null;
        await this.saveSettings({ eventRole: role });
        return menu(UPDATE_MESSAGE, 'manage', null, role ? t('eventRoleSet')(role) : t('eventRoleCleared'));
      }
      case 'pref': {
        const key = `pref:${p.user}`;
        const pref = (await this.ctx.storage.get(key)) ?? {};
        pref.dm = !pref.dm;
        await this.ctx.storage.put(key, pref);
        // No menu do canal, a resposta vai só pra quem clicou e o menu fica como está.
        if (p.pub) return note(pref.dm ? t('dmNowOn') : t('dmNowOff'));
        return menu(UPDATE_MESSAGE, 'events', null, pref.dm ? t('dmNowOn') : t('dmNowOff'));
      }
      case 'evnew': return this.newEvent(p, state, lang);
      case 'evmodal': {
        let draft = await this.draft(p.user, now);
        if (!draft || draft.type !== p.type) draft = newDraft(p.type, p.user);
        applyFields(draft, p.fields ?? {}, now);
        await this.saveDraft(p.user, draft, now);
        return update(screenMessage(draftReview(draft, lang, state.settings?.eventsChannel)));
      }
      case 'evdraft': return this.draftOp(p, state, lang, now);
      case 'signup': return this.signUp(p, state, lang);
      case 'evcancel': return this.cancelEvent(p, state, lang, now);
      default: return note(t('unknown'));
    }
  },

  // A mensagem do menu numa seção. Ajustes só para quem gerencia; quem não gerencia cai no painel.
  // Com p.pub, o menu do canal: nada nele depende de quem clicou.
  async menuData(view, arg, p, notice) {
    const now = nowSeconds();
    const state = await this.state();
    const lang = langOf(state);
    const settings = state.settings ?? {};
    const shown = view === 'manage' && (!p.manager || p.pub) ? 'panel' : view;
    const embed = await this.sectionEmbed(shown, arg, state, now, lang, p.pub ? { ...p, user: null } : p);
    const pref = shown === 'events' && !p.pub ? (await this.ctx.storage.get(`pref:${p.user}`)) ?? {} : {};
    return menuMessage(lang, {
      embed, notice, view: shown, arg, manager: p.manager, pub: Boolean(p.pub),
      weeklyOff: Boolean(state.weekly?.off), eventRole: settings.eventRole ?? null,
      canCreate: canCreate(p, settings), dmOn: Boolean(pref.dm),
    });
  },

  async sectionEmbed(view, arg, state, now, lang, p = {}) {
    const t = (key) => text(lang, key);
    const settings = state.settings ?? {};
    switch (view) {
      case 'subs': return sectionEmbed(t('subsTitle'), subsSummary(state, now));
      case 'supplies': return sectionEmbed(t('suppliesTitle'), suppliesSummary(state, now));
      case 'profit': return sectionEmbed(t('profitTitle'), profitSummary(await this.profit(now)));
      case 'manage': {
        const channels = t('channels')(settings.panelChannel, settings.alertsChannel, settings.eventsChannel);
        return sectionEmbed(t('manageTitle'), `${t('manageText')}\n\n${channels}`);
      }
      case 'events': {
        const lines = openEvents(await this.events()).map((e) => eventLine(e, lang, p.user));
        return sectionEmbed(t('eventsTitle'), lines.join('\n') || t('eventsEmpty'));
      }
      case 'loot': {
        const count = { week: 7, month: 30 }[arg] ?? 1;
        return sectionEmbed(t('lootTitle'), lootSummary(await this.voyagesSince(now, count), lootTitle(lang, count, now)));
      }
      default: return renderPanel(state, await this.voyagesOn(dayOf(now)), now).embeds[0];
    }
  },

  async saveSettings(changes) {
    const state = await this.state();
    state.settings = { ...(state.settings ?? {}), ...changes };
    await this.ctx.storage.put('state', state);
    return state;
  },

  // Ajustes de quem gerencia o servidor. A permissão já foi conferida antes de chegar aqui.
  async action(p, state, lang) {
    const t = (key) => text(lang, key);
    const manage = async (notice) => ({ response: { type: UPDATE_MESSAGE, data: await this.menuData('manage', null, p, notice) } });
    switch (p.name) {
      case 'repost':
        await this.repostPanel();
        return manage(t('panelReposted'));
      case 'regroup': {
        try {
          const result = await this.regroupAlerts();
          return manage(t('regroupDone')(result.removed, result.sent));
        } catch (error) {
          console.error('Falha ao agrupar os avisos', error);
          return manage(t('regroupFailed'));
        }
      }
      case 'weekly-on':
      case 'weekly-off': {
        const latest = await this.state();
        latest.weekly = { ...(latest.weekly ?? {}), off: p.name === 'weekly-off' };
        await this.ctx.storage.put('state', latest);
        // O resumo ligado ou desligado muda o próximo alarme.
        await this.ctx.storage.setAlarm(Date.now());
        return manage(p.name === 'weekly-off' ? t('weeklyNowOff') : t('weeklyNowOn'));
      }
      case 'lang-en':
      case 'lang-pt': {
        const next = p.name.slice(5);
        await this.saveSettings({ lang: next });
        // O painel do bot tem botões no idioma da FC.
        await this.ctx.storage.setAlarm(Date.now());
        return manage(text(next, 'langNow'));
      }
      case 'log': {
        const logs = [...(await this.ctx.storage.list({ prefix: 'log:' })).values()].sort((a, b) => b.uploadedAt - a.uploadedAt);
        const log = logs[0];
        if (!log) return note(t('noLog'));
        return {
          ...note(t('logOf')(log.character, log.uploadedAt)),
          file: { name: `log-${log.character.replace(/[^\w-]+/g, '_')}.txt`, text: log.text },
        };
      }
      case 'panel-here':
      case 'alerts-here':
      case 'events-here': {
        if (!this.env.DISCORD_BOT_TOKEN) return manage(t('noBot'));
        if (!p.channelId) return manage(t('unknown'));
        if (p.name === 'panel-here') {
          await this.movePanel(p.channelId);
          return manage(t('panelMoved')(p.channelId));
        }
        if (p.name === 'alerts-here') {
          await this.saveSettings({ alertsChannel: p.channelId });
          return manage(t('alertsMoved')(p.channelId));
        }
        await this.saveSettings({ eventsChannel: p.channelId });
        return manage(t('eventsMoved')(p.channelId));
      }
      default: return note(t('unknown'));
    }
  },

  // ---------- eventos ----------

  async draft(user, now) {
    const draft = await this.ctx.storage.get(`draft:${user}`);
    return draft && now - draft.savedAt < DRAFT_TTL ? draft : null;
  },

  async saveDraft(user, draft, now) {
    await this.ctx.storage.put(`draft:${user}`, { ...draft, savedAt: now });
  },

  // Antes de criar: permissão, bot e canal. Sem tipo, mostra a escolha; com tipo, abre o formulário.
  async newEvent(p, state, lang) {
    const t = (key) => text(lang, key);
    const settings = state.settings ?? {};
    if (!canCreate(p, settings)) return note(t('cantCreate')(settings.eventRole));
    if (!this.env.DISCORD_BOT_TOKEN || !settings.eventsChannel) {
      const notice = this.env.DISCORD_BOT_TOKEN ? t('noEventsChannel') : t('noBot');
      if (p.pub) return note(notice);
      return { response: { type: UPDATE_MESSAGE, data: await this.menuData('events', null, p, notice) } };
    }
    if (!p.type) {
      // Do menu do canal, a criação abre numa mensagem só de quem clicou, e segue nela até postar.
      const picker = typePicker(lang);
      const screen = screenMessage({ embeds: [picker.embed], components: picker.components });
      return p.pub ? { response: { type: REPLY, data: screen } } : update(screen);
    }
    const now = nowSeconds();
    const draft = newDraft(p.type, p.user);
    await this.saveDraft(p.user, draft, now);
    return { response: { type: MODAL, data: eventModal(draft, lang) } };
  },

  async draftOp(p, state, lang, now) {
    const t = (key) => text(lang, key);
    const settings = state.settings ?? {};
    const eventsMenu = async (notice) => ({ response: { type: UPDATE_MESSAGE, data: await this.menuData('events', null, p, notice) } });
    const draft = await this.draft(p.user, now);
    if (!draft) return eventsMenu(t('draftGone'));
    const review = (problem) => {
      const screen = draftReview(draft, lang, settings.eventsChannel);
      if (problem) screen.content = `⚠️ ${problem}`;
      return update(screenMessage(screen));
    };

    if (p.op === 'edit') return { response: { type: MODAL, data: eventModal(draft, lang) } };
    if (p.op === 'cancel') {
      await this.ctx.storage.delete(`draft:${p.user}`);
      return eventsMenu(t('draftCancelled'));
    }
    if (p.op !== 'post') {
      changeDraft(draft, p.op, p.value);
      await this.saveDraft(p.user, draft, now);
      return review();
    }

    // Postar: tudo é conferido de novo, porque o rascunho pode ser de horas atrás.
    if (!canCreate(p, settings)) return note(t('cantCreate')(settings.eventRole));
    if (draft.error) return review(t(draft.error));
    if (!(draft.start > now)) return review(t('pastWhen'));
    const transport = transportOf('events', settings, this.env);
    if (!transport) return eventsMenu(this.env.DISCORD_BOT_TOKEN ? t('noEventsChannel') : t('noBot'));
    if (openEvents(await this.events()).length >= MAX_OPEN) return review(t('tooMany'));

    const event = eventFromDraft(draft, newId(), now);
    Object.assign(event, { guildId: p.guildId, channelId: settings.eventsChannel });
    const sent = await transport.create(eventPost(event, lang, state));
    if (!sent.ok) {
      console.error(`Discord respondeu ${sent.status} ao postar o evento`);
      return review(t('postFailed'));
    }
    event.messageId = sent.id;
    const events = await this.events();
    events[event.id] = event;
    await this.ctx.storage.put('events', events);
    await this.ctx.storage.delete(`draft:${p.user}`);
    await this.wakeAt(eventsWakeAt({ [event.id]: event }, now));
    return eventsMenu(t('posted')(event.title, event.channelId));
  },

  // Clique num botão de inscrição: a resposta edita o próprio post, sem precisar do bot.
  async signUp(p, state, lang) {
    const t = (key) => text(lang, key);
    let result;
    const event = await this.changeEvent(p.id, (e) => { result = signUp(e, p.user, p.choice); });
    if (!event || result.outcome === 'closed') return note(t('eventClosed'));

    const out = update(eventPost(event, lang, state));
    if (result.outcome === 'reserve') {
      out.followup = { content: t('inReserve')(result.position, result.role ? t('role')[result.role] : null), flags: EPHEMERAL };
    }
    if (result.promoted.length > 0) {
      const roleOf = new Map(result.promoted.map((x) => [x.user, t('role')[x.role]]));
      this.ctx.waitUntil(this.dmEach([...roleOf.keys()], (user) => t('promotedDm')(event.title, roleOf.get(user), event.start)));
    }
    return out;
  },

  async cancelEvent(p, state, lang, now) {
    const t = (key) => text(lang, key);
    const events = await this.events();
    const current = events[p.id];
    if (!current || current.status !== 'open') return note(t('eventClosed'));
    if (!(p.manager || current.by === p.user)) return note(t('notYours'));
    const event = await this.changeEvent(p.id, (e) => { Object.assign(e, { status: 'cancelled', closedAt: now }); });
    this.ctx.waitUntil(this.notifyEvent(event, state, lang, (l) => text(l, 'cancelledDm')(event.title, event.start), [...attendees(event), ...event.reserve.map((r) => r.user)]));
    return update(eventPost(event, lang, state));
  },

  // DM para cada pessoa que ligou o lembrete por DM. Quem não ligou não recebe nada.
  async dmEach(users, build) {
    if (!this.env.DISCORD_BOT_TOKEN || users.length === 0) return;
    const prefs = await this.ctx.storage.get(users.slice(0, 128).map((u) => `pref:${u}`));
    for (const user of users.slice(0, 128)) {
      if (!prefs.get(`pref:${user}`)?.dm) continue;
      const payload = { content: build(user), allowed_mentions: { parse: [] } };
      const sent = await sendDm(this.env.DISCORD_BOT_TOKEN, user, payload, this.env.DISCORD_API ?? DISCORD_API);
      if (!sent.ok) console.error(`Discord respondeu ${sent.status} à DM`);
    }
  },

  // Aviso de um evento: no canal dos avisos (marcando quem vai) e/ou por DM, conforme o evento.
  async notifyEvent(event, state, lang, content, users = attendees(event)) {
    const settings = state.settings ?? {};
    const body = content(lang);
    if (event.notify !== 'dm') {
      const transport = transportOf('alerts', settings, this.env);
      if (transport) {
        const mentions = users.slice(0, 50);
        const link = event.messageId && event.guildId ? `\nhttps://discord.com/channels/${event.guildId}/${event.channelId}/${event.messageId}` : '';
        const sent = await transport.create({
          content: `${body}${mentions.length ? ` ${mentions.map((u) => `<@${u}>`).join(' ')}` : ''}${link}`,
          allowed_mentions: { parse: [], users: mentions },
        });
        if (sent.ok) {
          const now = nowSeconds();
          const latest = await this.state();
          latest.alerts = [...(latest.alerts ?? []), { id: sent.id, deleteAt: alertDeleteAt(now), kind: 'event', via: transport.via }];
          await this.ctx.storage.put('state', latest);
        } else {
          console.error(`Discord respondeu ${sent.status} ao avisar o evento`);
        }
      }
    }
    if (event.notify !== 'channel') await this.dmEach(users, () => body);
  },

  // No alarme: lembretes, fechar o que já passou, postar a próxima semana e tirar os fechados antigos.
  // Devolve o próximo momento em que algum evento precisa de algo (unix), ou Infinity.
  async runEvents(now) {
    const state = await this.state();
    const lang = langOf(state);
    const settings = state.settings ?? {};
    for (const event of Object.values(await this.events())) {
      const due = dueReminders(event, now);
      if (due.length > 0) {
        // Marca antes de mandar: um lembrete a menos é melhor que dois iguais.
        const marked = await this.changeEvent(event.id, (e) => { e.sent = [...new Set([...e.sent, ...due])]; });
        await this.notifyEvent(marked, state, lang, (l) => text(l, 'reminder')(event.title, event.start));
      }
      if (closeDue(event, now)) {
        const closed = await this.changeEvent(event.id, (e) => { Object.assign(e, { status: 'done', closedAt: now }); });
        const transport = transportOf('events', settings, this.env);
        if (transport && closed.messageId) await transport.edit(closed.messageId, eventPost(closed, lang, state));
        if (closed.repeat && transport) {
          const next = nextOccurrence(closed, newId(), now);
          next.channelId = settings.eventsChannel;
          const sent = await transport.create(eventPost(next, lang, state));
          if (sent.ok) {
            next.messageId = sent.id;
            const events = await this.events();
            events[next.id] = next;
            await this.ctx.storage.put('events', events);
          } else {
            console.error(`Discord respondeu ${sent.status} ao postar a próxima semana`);
          }
        }
      }
      if (pruneDue(event, now)) {
        const events = await this.events();
        delete events[event.id];
        await this.ctx.storage.put('events', events);
      }
    }
    return eventsWakeAt(await this.events(), now);
  },
};
