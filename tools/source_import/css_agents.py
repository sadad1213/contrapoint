"""Stock CSS player model names and animation include dependencies."""
T_AGENTS = ('phoenix', 'arctic', 'guerilla', 'leet')
CT_AGENTS = ('urban', 'gsg9', 'sas', 'gign')
AGENTS = T_AGENTS + CT_AGENTS


def model_name(agent):
    return ('ct_' if agent in CT_AGENTS else 't_') + agent


def animation_models(agent):
    return ('models/player/cs_player_shared.mdl',
            'models/player/ak_anims_ct.mdl' if agent in CT_AGENTS else 'models/player/ak_anims_t.mdl')
